using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aster.AspNetCore.Tests;

/// <summary>
/// #108: <c>MapAster</c> serves definitions, shared aspects, resources and schemas over HTTP, with ProblemDetails
/// errors, opt-in groups, read-only mode, host authorization, resource-level authorization and tenant resolution.
/// </summary>
public sealed class AsterEndpointsTests : IAsyncLifetime
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly List<WebApplication> apps = [];
    private readonly List<HttpClient> clients = [];
    private readonly List<string> databases = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var client in clients)
            client.Dispose();
        foreach (var app in apps)
            await app.DisposeAsync();
        foreach (var database in databases)
        {
            foreach (var path in new[] { database, $"{database}-wal", $"{database}-shm" })
                File.Delete(path);
        }
    }

    private async Task<(HttpClient Client, IServiceProvider Services)> StartAsync(
        string provider = InMemory,
        Action<AsterEndpointOptions>? configure = null,
        Action<IServiceCollection>? services = null,
        Action<WebApplication>? map = null,
        bool loopback = false,
        string? pathBase = null)
    {
        var builder = WebApplication.CreateBuilder();

        // Kestrel on a loopback port, for what TestServer doesn't model, such as the raw request target.
        if (loopback)
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        else
            builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAsterCore();
        if (provider == Sqlite)
        {
            var path = Path.Combine(Path.GetTempPath(), $"aster-http-{Guid.NewGuid():N}.db");
            databases.Add(path);
            builder.Services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={path};Pooling=False");
        }

        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticated>("test", null);
        builder.Services.AddAuthorization(options => options.AddPolicy("deny", policy => policy.RequireAssertion(static _ => false)));
        services?.Invoke(builder.Services);

        var app = builder.Build();
        if (pathBase is not null)
        {
            app.UsePathBase(pathBase);
            app.UseRouting();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAster("/api/aster", configure);
        map?.Invoke(app);
        await app.StartAsync();
        apps.Add(app);

        var client = loopback ? new HttpClient { BaseAddress = new Uri(app.Urls.Single()) } : app.GetTestClient();
        clients.Add(client);
        return (client, app.Services);
    }

    private static ResourceDefinition Article() => new ResourceDefinitionBuilder()
        .WithDefinitionId("Article")
        .WithAspect("Content")
            .WithFacet("Title", "string", isRequired: true)
            .WithFacetMetadata(new FacetMetadata { Label = "Title", MaxLength = 20 })
            .WithFacet("Views", "int")
        .Build();

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, ResourceDefinition definition, int? expected = null) =>
        await client.PostAsJsonAsync($"/api/aster/definitions{(expected is { } e ? $"?expectedLatestVersion={e}" : "")}", definition, Web);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static object Content(string title, int? views = null) =>
        new Dictionary<string, object?> { ["Content"] = new Dictionary<string, object?> { ["Title"] = title, ["Views"] = views } };

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string resourceId, string title, int? views = null) =>
        await client.PostAsJsonAsync("/api/aster/resources", new { definitionId = "Article", resourceId, aspects = Content(title, views) }, Web);

    // ──────────────────────────────────────────────────────────────────────────
    // Definitions and schemas
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Definitions_RegisterListGetAndExportSchema()
    {
        var (client, _) = await StartAsync();

        var created = await RegisterAsync(client, Article(), expected: 0);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/aster/definitions/Article", created.Headers.Location?.OriginalString);
        Assert.Equal(1, (await ReadAsync(created)).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client, Article())).StatusCode);

        var list = await ReadAsync(await client.GetAsync("/api/aster/definitions"));
        Assert.Equal(2, Assert.Single(list.EnumerateArray()).GetProperty("version").GetInt32());
        Assert.Equal(1, (await ReadAsync(await client.GetAsync("/api/aster/definitions/Article?version=1"))).GetProperty("version").GetInt32());

        var schemaResponse = await client.GetAsync("/api/aster/definitions/Article/schema");
        Assert.Equal("application/schema+json", schemaResponse.Content.Headers.ContentType?.MediaType);
        var schema = await ReadAsync(schemaResponse);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.GetProperty("$schema").GetString());
        Assert.Equal("Title", schema.GetProperty("properties").GetProperty("Content").GetProperty("required")[0].GetString());
    }

    [Fact]
    public async Task Definitions_ConflictAndValidationAndNotFound_AreProblemDetails()
    {
        var (client, _) = await StartAsync();
        await RegisterAsync(client, Article(), expected: 0);

        var conflict = await RegisterAsync(client, Article(), expected: 0);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
        Assert.Equal("definition-version-conflict", (await ReadAsync(conflict)).GetProperty("code").GetString());

        var invalid = new ResourceDefinitionBuilder().WithDefinitionId("Broken").WithAspect("A").WithFacet("F", "strnig").Build();
        var badRequest = await RegisterAsync(client, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        var diagnostic = (await ReadAsync(badRequest)).GetProperty("diagnostics")[0];
        Assert.Equal(("facet-type-unknown", "A", "F"), (diagnostic.GetProperty("code").GetString(), diagnostic.GetProperty("aspectKey").GetString(), diagnostic.GetProperty("facetId").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/aster/definitions/Nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/aster/definitions/Article?version=9")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/aster/definitions/Nope/schema")).StatusCode);
    }

    [Fact]
    public async Task SchemaUpgradeRejections_ThrownInsideTheAsterGroup_AreProblemDetails()
    {
        // A host endpoint mapped into the resources group that prefers exception flow (#188).
        var (client, _) = await StartAsync(configure: options => options.ConfigureGroup = static (group, builder) =>
        {
            if (group == AsterEndpointGroups.Resources)
                builder.MapPost("/{resourceId}/schema-upgrade", static async (string resourceId, int baseVersion, IResourceSchemaVersionService schemaVersions, CancellationToken ct) =>
                    Results.Ok((await schemaVersions.UpgradeAsync(resourceId, new ResourceSchemaUpgradeRequest { BaseVersion = baseVersion }, ct)).ThrowIfRejected().Status));
        });
        await RegisterAsync(client, Article(), expected: 0);
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(client, "a1", "Hello")).StatusCode);
        var stricter = new ResourceDefinitionBuilder()
            .WithDefinitionId("Article")
            .WithAspect("Content")
                .WithFacet("Title", "string", isRequired: true)
                .WithFacetMetadata(new FacetMetadata { Label = "Title", MaxLength = 3 })
                .WithFacet("Views", "int")
            .Build();
        Assert.True((await RegisterAsync(client, stricter, expected: 1)).IsSuccessStatusCode);

        var invalid = await client.PostAsync("/api/aster/resources/a1/schema-upgrade?baseVersion=1", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var diagnostic = (await ReadAsync(invalid)).GetProperty("diagnostics")[0];
        Assert.Equal(
            ("facet-value-length-out-of-range", "Content", "Title"),
            (diagnostic.GetProperty("code").GetString(), diagnostic.GetProperty("aspectKey").GetString(), diagnostic.GetProperty("facetId").GetString()));

        var stale = await client.PostAsync("/api/aster/resources/a1/schema-upgrade?baseVersion=9", content: null);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency-conflict", (await ReadAsync(stale)).GetProperty("code").GetString());

        var missing = await client.PostAsync("/api/aster/resources/nope/schema-upgrade?baseVersion=1", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shared aspects
    // ──────────────────────────────────────────────────────────────────────────

    private static AspectDefinition Seo(params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = "Seo",
        Id = Guid.NewGuid().ToString("N"),
        Version = 0,
        FacetDefinitions = facets.Length > 0 ? facets : [new FacetDefinition { FacetDefinitionId = "Slug", Id = "f", Version = 1, Type = "string", IsRequired = true }],
    };

    [Fact]
    public async Task Aspects_ExpectedLatestVersion_RegistersOnlyOnTheExpectedVersion()
    {
        var (client, _) = await StartAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/aster/aspects?expectedLatestVersion=0", Seo(), Web)).StatusCode);

        var exists = await client.PostAsJsonAsync("/api/aster/aspects?expectedLatestVersion=0", Seo(), Web);
        Assert.Equal(HttpStatusCode.Conflict, exists.StatusCode);
        Assert.Equal("aspect-version-conflict", (await ReadAsync(exists)).GetProperty("code").GetString());

        Assert.Equal(2, (await ReadAsync(await client.PostAsJsonAsync("/api/aster/aspects?expectedLatestVersion=1", Seo(), Web))).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/aster/aspects?expectedLatestVersion=1", Seo(), Web)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/aster/aspects?expectedLatestVersion=-1", Seo(), Web)).StatusCode);
        Assert.Equal(2, (await ReadAsync(await client.GetAsync("/api/aster/aspects/Seo/versions"))).GetArrayLength());
    }

    [Fact]
    public async Task Aspects_RegisterVersionsAndPinWithTheStoredCopy()
    {
        var (client, _) = await StartAsync();

        Assert.Equal(1, (await ReadAsync(await client.PostAsJsonAsync("/api/aster/aspects", Seo(), Web))).GetProperty("version").GetInt32());
        Assert.Equal(2, (await ReadAsync(await client.PostAsJsonAsync("/api/aster/aspects", Seo(), Web))).GetProperty("version").GetInt32());
        Assert.Equal(2, (await ReadAsync(await client.GetAsync("/api/aster/aspects/Seo/versions"))).GetArrayLength());
        Assert.Equal("object", (await ReadAsync(await client.GetAsync("/api/aster/aspects/Seo/schema"))).GetProperty("type").GetString());

        // The client claims version 1 of Seo but sends different facets: the stored version 1 is embedded instead.
        var tampered = Seo(new FacetDefinition { FacetDefinitionId = "Injected", Id = "x", Version = 1, Type = "string" }) with { Version = 1, IsShared = true };
        var definition = new ResourceDefinitionBuilder().WithDefinitionId("Page").Build() with
        {
            AspectDefinitions = new Dictionary<string, AspectDefinition> { ["Seo"] = tampered },
        };
        var registered = await ReadAsync(await RegisterAsync(client, definition));
        var pinned = registered.GetProperty("aspectDefinitions").GetProperty("Seo");
        Assert.True(pinned.GetProperty("isShared").GetBoolean());
        Assert.Equal("Slug", pinned.GetProperty("facetDefinitions")[0].GetProperty("facetDefinitionId").GetString());

        var missing = definition with { DefinitionId = "Other", AspectDefinitions = new Dictionary<string, AspectDefinition> { ["Seo"] = tampered with { Version = 7 } } };
        var problem = await RegisterAsync(client, missing);
        Assert.Equal(HttpStatusCode.BadRequest, problem.StatusCode);
        Assert.Equal("aspect-definition-not-found", (await ReadAsync(problem)).GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Resources
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Resources_CreateUpdateHistoryAndActivation(string provider)
    {
        var (client, _) = await StartAsync(provider);
        await RegisterAsync(client, Article());

        var created = await CreateAsync(client, "a-1", "Hello", 3);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/aster/resources/a-1", created.Headers.Location?.OriginalString);

        var invalid = await client.PostAsJsonAsync("/api/aster/resources", new { definitionId = "Article", aspects = Content(new string('x', 30)) }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Content", (await ReadAsync(invalid)).GetProperty("diagnostics")[0].GetProperty("aspectKey").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync(client, "a-1", "Again")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/aster/resources", new { definitionId = "Nope", aspects = Content("x") }, Web)).StatusCode);

        var updated = await client.PutAsJsonAsync("/api/aster/resources/a-1", new { baseVersion = 1, aspects = Content("Hello again") }, Web);
        Assert.Equal(2, (await ReadAsync(updated)).GetProperty("version").GetInt32());
        var stale = await client.PutAsJsonAsync("/api/aster/resources/a-1", new { baseVersion = 1, aspects = Content("Late") }, Web);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency-conflict", (await ReadAsync(stale)).GetProperty("code").GetString());

        var first = await ReadAsync(await client.GetAsync("/api/aster/resources/a-1?version=1"));
        Assert.Equal("Hello", first.GetProperty("aspects").GetProperty("Content").GetProperty("Title").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/aster/resources/a-1/activate", new { version = 1, channel = "Published" }, Web)).StatusCode);
        var history = await ReadAsync(await client.GetAsync("/api/aster/resources/a-1/versions"));
        var versions = history.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.Contains(versions, static version => version.GetProperty("version").GetInt32() == 1
            && version.GetProperty("activeChannels").EnumerateArray().Any(static channel => channel.GetString() == "Published"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/aster/resources/a-1/deactivate", new { version = 1, channel = "Published" }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/aster/resources/a-1/activate", new { version = 9, channel = "Published" }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/aster/resources/nope")).StatusCode);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Resources_ListExcludesArchivedUnlessAsked(string provider)
    {
        var (client, services) = await StartAsync(provider);
        await RegisterAsync(client, Article());
        await CreateAsync(client, "a-1", "One");
        await CreateAsync(client, "a-2", "Two");
        await services.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "a-2",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        async Task<string[]> Ids(string query) =>
            (await ReadAsync(await client.GetAsync($"/api/aster/resources?definitionId=Article{query}")))
                .GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("resourceId").GetString()!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "a-1" }, await Ids(""));
        Assert.Equal(new[] { "a-1", "a-2" }, await Ids("&lifecycle=all"));
        Assert.Equal(new[] { "a-2" }, await Ids("&lifecycle=archived"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/aster/resources?lifecycle=gone")).StatusCode);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Query_TranslatesFiltersSortsAndPaging(string provider)
    {
        var (client, _) = await StartAsync(provider);
        await RegisterAsync(client, Article());
        await CreateAsync(client, "a-1", "Alpha", 10);
        await CreateAsync(client, "a-2", "Beta", 20);
        await CreateAsync(client, "a-3", "Gamma", 30);

        async Task<JsonElement> Query(object body) =>
            await ReadAsync(await client.PostAsJsonAsync("/api/aster/resources/query", body, Web));

        var range = await Query(new
        {
            definitionId = "Article",
            filter = new { aspect = "Content", facet = "Views", op = "range", value = new { min = 15, max = 30, includeMax = false } },
        });
        Assert.Equal(new[] { "a-2" }, range.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("resourceId").GetString()));

        var logical = await Query(new
        {
            filter = new
            {
                or = new object[]
                {
                    new { field = "ResourceId", op = "equals", value = "a-1" },
                    new { and = new object[] { new { exists = "Content" }, new { aspect = "Content", facet = "Title", op = "in", value = new[] { "Gamma" } } } },
                },
            },
            sort = new[] { new { field = "ResourceId", descending = true } },
        });
        Assert.Equal(new[] { "a-3", "a-1" }, logical.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("resourceId").GetString()));

        var page = await Query(new { sort = new[] { new { field = "ResourceId" } }, skip = 1, take = 1 });
        Assert.Equal("a-2", page.GetProperty("items")[0].GetProperty("resourceId").GetString());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.False((await Query(new { skip = 2, take = 1 })).GetProperty("hasMore").GetBoolean());

        var badOperator = await client.PostAsJsonAsync("/api/aster/resources/query", new { filter = new { field = "ResourceId", op = "like", value = "a" } }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, badOperator.StatusCode);
        Assert.Equal("query-operator-invalid", (await ReadAsync(badOperator)).GetProperty("diagnostics")[0].GetProperty("code").GetString());

        var twoShapes = await client.PostAsJsonAsync("/api/aster/resources/query", new { filter = new { exists = "Content", field = "ResourceId" } }, Web);
        Assert.Equal("query-filter-invalid", (await ReadAsync(twoShapes)).GetProperty("diagnostics")[0].GetProperty("code").GetString());

        var nullSort = await client.PostAsJsonAsync("/api/aster/resources/query", new { sort = new object?[] { null } }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, nullSort.StatusCode);
        Assert.Equal("query-sort-invalid", (await ReadAsync(nullSort)).GetProperty("diagnostics")[0].GetProperty("code").GetString());

        var nullOperand = await client.PostAsJsonAsync("/api/aster/resources/query", new { filter = new { and = new object?[] { null } } }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, nullOperand.StatusCode);
        Assert.Equal("query-filter-invalid", (await ReadAsync(nullOperand)).GetProperty("diagnostics")[0].GetProperty("code").GetString());

        var tooLarge = await client.PostAsJsonAsync("/api/aster/resources/query", new { take = 501 }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Query_ReportsEveryValidationFailure_WithRequestPaths(string provider)
    {
        var (client, _) = await StartAsync(provider);
        var body = new
        {
            scope = "active",
            filter = new { and = new object[] { new { exists = "Content" }, new { field = "Bogus", op = "equals", value = "x" } } },
            sort = new[] { new { field = "ResourceId" }, new { field = "Bogus" } },
        };

        var query = await client.PostAsJsonAsync("/api/aster/resources/query", body, Web);
        var validate = await client.PostAsJsonAsync("/api/aster/resources/query/validate", body, Web);

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        var expected = new (string?, string?, string?)[]
        {
            ("activation-channel-required", "channel", "scope"),
            ("unsupported-metadata-field", "filter.and[1].field", "metadata field"),
            ("unsupported-metadata-sort-field", "sort[1].field", "metadata field"),
        };
        Assert.Equal(expected, Diagnostics((await ReadAsync(query)).GetProperty("diagnostics")));
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var validation = await ReadAsync(validate);
        Assert.False(validation.GetProperty("isValid").GetBoolean());
        Assert.Equal(expected, Diagnostics(validation.GetProperty("diagnostics")));

        // The wire format's own errors carry their path too, and a query that passes reports no diagnostics.
        var badOperator = await ReadAsync(await client.PostAsJsonAsync("/api/aster/resources/query/validate", new { filter = new { or = new object[] { new { exists = "Content" }, new { field = "ResourceId", op = "like", value = "a" } } } }, Web));
        Assert.Equal(("query-operator-invalid", "filter.or[1].op"), (badOperator.GetProperty("diagnostics")[0].GetProperty("code").GetString(), badOperator.GetProperty("diagnostics")[0].GetProperty("path").GetString()));
        var badBound = await ReadAsync(await client.PostAsJsonAsync("/api/aster/resources/query/validate", new { filter = new { not = new { aspect = "Content", facet = "Views", op = "range", value = new { min = true } } } }, Web));
        Assert.Equal("filter.not.value.min", badBound.GetProperty("diagnostics")[0].GetProperty("path").GetString());
        var valid = await ReadAsync(await client.PostAsJsonAsync("/api/aster/resources/query/validate", new { filter = new { exists = "Content" } }, Web));
        Assert.True(valid.GetProperty("isValid").GetBoolean());
        Assert.Empty(valid.GetProperty("diagnostics").EnumerateArray());

        static (string?, string?, string?)[] Diagnostics(JsonElement diagnostics) =>
            diagnostics.EnumerateArray()
                .Select(static d => (d.GetProperty("code").GetString(), d.GetProperty("path").GetString(), d.GetProperty("feature").GetString()))
                .ToArray();
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Query_ReferencesFilter_MatchesReferenceAndReferenceArrayFacets(string provider)
    {
        var (client, _) = await StartAsync(provider);
        await RegisterAsync(client, Article());
        await CreateAsync(client, "a-1", "Alpha");
        await CreateAsync(client, "a-2", "Beta");
        await RegisterAsync(client, new ResourceDefinitionBuilder()
            .WithDefinitionId("Note")
            .WithAspect("Links")
                .WithFacet("Primary", "reference")
                .WithFacet("Related", "reference[]")
            .Build());
        async Task CreateNoteAsync(string resourceId, string? primary, params string[] related) =>
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/aster/resources", new
            {
                definitionId = "Note",
                resourceId,
                aspects = new Dictionary<string, object?> { ["Links"] = new Dictionary<string, object?> { ["Primary"] = primary, ["Related"] = related } },
            }, Web)).StatusCode);
        await CreateNoteAsync("n-1", "a-1");
        await CreateNoteAsync("n-2", "a-2", "a-1");
        await CreateNoteAsync("n-3", "a-2");

        async Task<string?[]> Referrers(string facet) =>
            (await ReadAsync(await client.PostAsJsonAsync("/api/aster/resources/query", new
            {
                definitionId = "Note",
                filter = new { aspect = "Links", facet, references = "a-1" },
                sort = new[] { new { field = "ResourceId" } },
            }, Web))).GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("resourceId").GetString()).ToArray();

        Assert.Equal(new[] { "n-1" }, await Referrers("Primary"));
        Assert.Equal(new[] { "n-2" }, await Referrers("Related"));

        var withOp = await client.PostAsJsonAsync("/api/aster/resources/query", new { filter = new { aspect = "Links", facet = "Primary", references = "a-1", op = "equals" } }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, withOp.StatusCode);
        var diagnostic = (await ReadAsync(withOp)).GetProperty("diagnostics")[0];
        Assert.Equal(("query-filter-invalid", "filter"), (diagnostic.GetProperty("code").GetString(), diagnostic.GetProperty("path").GetString()));

        // A blank member is reported at that member.
        foreach (var (body, expectedPath) in new (object, string)[]
        {
            (new { aspect = " ", facet = "Primary", references = "a-1" }, "filter.aspect"),
            (new { aspect = "Links", facet = " ", references = "a-1" }, "filter.facet"),
            (new { aspect = "Links", facet = "Primary", references = " " }, "filter.references"),
        })
        {
            var blank = (await ReadAsync(await client.PostAsJsonAsync("/api/aster/resources/query/validate", new { filter = body }, Web))).GetProperty("diagnostics")[0];
            Assert.Equal(("query-value-required", expectedPath), (blank.GetProperty("code").GetString(), blank.GetProperty("path").GetString()));
        }
    }

    [Theory]
    [InlineData(InMemory, "in-memory")]
    [InlineData(Sqlite, "sqlite-json")]
    public async Task Query_Capabilities_DescribeTheActiveProvider(string provider, string providerKey)
    {
        var (client, services) = await StartAsync(provider);

        var capabilities = await ReadAsync(await client.GetAsync("/api/aster/resources/query/capabilities"));

        Assert.Equal(providerKey, capabilities.GetProperty("providerKey").GetString());
        Assert.Equal(services.GetRequiredService<IResourceQueryValidator>().Capabilities!.ProviderName, capabilities.GetProperty("providerName").GetString());
    }

    [Fact]
    public async Task Query_Capabilities_NotFound_WhenTheValidatorDeclaresNone()
    {
        // A validator written before IResourceQueryValidator.Capabilities existed still compiles and reports none.
        var (client, _) = await StartAsync(services: static services => services.AddSingleton<IResourceQueryValidator, AcceptEverything>());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/aster/resources/query/capabilities")).StatusCode);
    }

    private sealed class AcceptEverything : IResourceQueryValidator
    {
        public QueryValidationResult Validate(ResourceQuery query) => QueryValidationResult.Success;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Options and host hooks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadOnlyAndGroups_LimitWhatIsMapped()
    {
        var (readOnly, _) = await StartAsync(configure: static options => options.ReadOnly = true);
        Assert.Equal(HttpStatusCode.OK, (await readOnly.GetAsync("/api/aster/definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await RegisterAsync(readOnly, Article())).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await CreateAsync(readOnly, "a-1", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await readOnly.PostAsJsonAsync("/api/aster/resources/query", new { }, Web)).StatusCode);

        var (resourcesOnly, _) = await StartAsync(configure: static options => options.Groups = AsterEndpointGroups.Resources);
        Assert.Equal(HttpStatusCode.NotFound, (await resourcesOnly.GetAsync("/api/aster/definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await resourcesOnly.GetAsync("/api/aster/resources")).StatusCode);
    }

    [Fact]
    public async Task GroupPolicies_ApplyPerGroup()
    {
        var (client, _) = await StartAsync(configure: static options => options.AuthorizationPolicies[AsterEndpointGroups.Definitions] = "deny");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/aster/definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/aster/aspects")).StatusCode);
    }

    [Fact]
    public async Task ResourceAuthorizer_DeniesSingleReadsAndFiltersLists()
    {
        var (client, _) = await StartAsync(services: static services => services.AddSingleton<IAsterResourceAuthorizer, HideSecrets>());
        await RegisterAsync(client, Article());
        await CreateAsync(client, "public", "Open");
        await CreateAsync(client, "secret", "Closed");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/aster/resources/secret")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/aster/resources/secret", new { baseVersion = 1, aspects = Content("x") }, Web)).StatusCode);
        var list = await ReadAsync(await client.GetAsync("/api/aster/resources"));
        Assert.Equal(new[] { "public" }, list.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("resourceId").GetString()));
    }

    [Fact]
    public async Task TenantAccessor_ScopesEveryEndpoint()
    {
        var (client, _) = await StartAsync(services: static services => services.AddSingleton<ITenantScopeAccessor, HeaderTenant>());

        async Task<HttpResponseMessage> Send(HttpMethod method, string uri, string tenant, object? body = null)
        {
            using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body, options: Web) };
            request.Headers.Add("X-Tenant", tenant);
            return await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/aster/definitions", "a", Article())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/aster/resources", "a", new { definitionId = "Article", resourceId = "r", aspects = Content("A") })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/aster/resources/r", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/aster/resources/r", "b")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/aster/definitions/Article", "b")).StatusCode);
        Assert.Equal(0, (await ReadAsync(await Send(HttpMethod.Get, "/api/aster/resources", "b"))).GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task PaddedIdentifiers_AreBadRequestsWithTheStableCode(string provider)
    {
        var (client, _) = await StartAsync(provider, services: static services => services.AddSingleton<ITenantScopeAccessor, HeaderTenant>());

        async Task<HttpResponseMessage> Send(string uri, object body, string tenant = "a")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, options: Web) };
            request.Headers.Add("X-Tenant", tenant);
            return await client.SendAsync(request);
        }

        async Task AssertPadded(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("identifier-whitespace", (await ReadAsync(response)).GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.Created, (await Send("/api/aster/definitions", Article())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send("/api/aster/resources", new { definitionId = "Article", resourceId = "a-1", aspects = Content("A") })).StatusCode);

        await AssertPadded(await Send("/api/aster/definitions", Article() with { DefinitionId = "Article " }));
        await AssertPadded(await Send("/api/aster/aspects", Seo() with { AspectDefinitionId = " Seo" }));
        await AssertPadded(await Send("/api/aster/resources", new { definitionId = "Article", resourceId = "a-2 ", aspects = Content("A") }));
        await AssertPadded(await Send("/api/aster/resources", new { definitionId = "Article ", resourceId = "a-3", aspects = Content("A") }));
        await AssertPadded(await Send("/api/aster/resources/a-1/activate", new { version = 1, channel = "Published " }));
        await AssertPadded(await Send("/api/aster/resources", new { definitionId = "Article", resourceId = "a-4", aspects = Content("A") }, tenant: "a "));

        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/aster/resources");
        get.Headers.Add("X-Tenant", "a");
        var items = (await ReadAsync(await client.SendAsync(get))).GetProperty("items").EnumerateArray()
            .Select(static item => item.GetProperty("resourceId").GetString());
        Assert.Equal(["a-1"], items);
    }

    [Fact]
    public async Task EveryEndpoint_CarriesOpenApiMetadata()
    {
        var (_, services) = await StartAsync();
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/aster", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(19, endpoints.Count);
        Assert.All(endpoints, static endpoint =>
        {
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary), endpoint.DisplayName);
            Assert.NotEmpty(endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags ?? []);
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // JSON wire format (#190)
    // ──────────────────────────────────────────────────────────────────────────

    private const string PruningPolicyByName =
        """[{"policyId":"prune","kind":"versionPruning","target":"resourceVersion","outcome":"prunePreview","criteria":{"maximumRetainedVersions":3}}]""";

    private static string ArticleJson(string policyDeclarations)
    {
        var definition = JsonSerializer.SerializeToNode(Article(), Web)!.AsObject();
        definition["policyDeclarations"] = JsonNode.Parse(policyDeclarations);
        return definition.ToJsonString();
    }

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string uri, string body, string contentType = "application/json") =>
        client.PostAsync(uri, new StringContent(body, Encoding.UTF8, contentType));

    /// <summary>Registers Article with a pruning policy, saves two versions of <c>a-1</c> and archives it.</summary>
    private static async Task<(string Definition, string History)> SeedHistoryAsync(HttpClient client, IServiceProvider services, string policyDeclarations = PruningPolicyByName)
    {
        var registered = await PostRawAsync(client, "/api/aster/definitions", ArticleJson(policyDeclarations));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal("/api/aster/definitions/Article", registered.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(client, "a-1", "One")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/aster/resources/a-1", new { baseVersion = 1, aspects = Content("Two") }, Web)).StatusCode);
        await services.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "a-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        return (await registered.Content.ReadAsStringAsync(), await client.GetStringAsync("/api/aster/resources/a-1/versions"));
    }

    [Fact]
    public async Task Json_WithoutHostOptions_EnumsAreCamelCaseNames()
    {
        var (client, services) = await StartAsync();

        var (definition, history) = await SeedHistoryAsync(client, services);

        Assert.Contains("\"kind\":\"versionPruning\",\"target\":\"resourceVersion\",\"outcome\":\"prunePreview\"", definition);
        Assert.Contains("\"lifecycleState\":\"archived\"", history);
        Assert.Contains("\"maintenanceDisposition\":\"protected\"", history);
        Assert.Contains("\"maintenanceDisposition\":\"possibleCandidate\"", history);
        Assert.Contains("\"kind\":\"versionPruning\"", await client.GetStringAsync("/api/aster/definitions/Article"));
    }

    [Fact]
    public async Task Json_HostJsonOptions_DoNotChangeTheAsterWireFormat()
    {
        // Host options that would misread camelCase bodies and write SNAKE_CASE members with KEBAB-CASE enums.
        var (client, services) = await StartAsync(
            services: static services =>
            {
                services.AddProblemDetails();
                services.ConfigureHttpJsonOptions(static options =>
                {
                    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper;
                    options.SerializerOptions.PropertyNameCaseInsensitive = false;
                    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseUpper));
                });
            },
            map: static app => app.MapGet("/host", static () => new { LifecycleState = ResourceLifecycleMarkerState.SoftDeleted }));

        var (definition, history) = await SeedHistoryAsync(client, services);

        Assert.Contains("\"definitionId\":\"Article\"", definition);
        Assert.Contains("\"kind\":\"versionPruning\"", definition);
        Assert.Contains("\"resourceId\":\"a-1\"", history);
        Assert.Contains("\"lifecycleState\":\"archived\"", history);
        Assert.Contains("\"maintenanceDisposition\":\"possibleCandidate\"", history);

        // Problem details keep their standard members and Aster's camelCase diagnostics.
        var invalid = await client.PostAsJsonAsync("/api/aster/resources", new { definitionId = "Article", aspects = Content(new string('x', 30)) }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        var problem = await ReadAsync(invalid);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Content", problem.GetProperty("diagnostics")[0].GetProperty("aspectKey").GetString());

        // The host's own endpoints still use the host's options.
        Assert.Equal("{\"LIFECYCLE_STATE\":\"SOFT-DELETED\"}", await client.GetStringAsync("/host"));
    }

    [Fact]
    public async Task Json_HostCanReplaceTheSerializerOptions()
    {
        var kebab = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) } };
        var (client, services) = await StartAsync(configure: options => options.SerializerOptions = kebab);
        Assert.True(kebab.IsReadOnly);

        var (definition, history) = await SeedHistoryAsync(
            client,
            services,
            """[{"policyId":"prune","kind":"version-pruning","target":"resource-version","outcome":"prune-preview","criteria":{"maximumRetainedVersions":3}}]""");

        Assert.Contains("\"kind\":\"version-pruning\",\"target\":\"resource-version\",\"outcome\":\"prune-preview\"", definition);
        Assert.Contains("\"maintenanceDisposition\":\"possible-candidate\"", history);
    }

    [Fact]
    public async Task Json_RequestBodies_AcceptEnumIntegersAndReportUnreadableBodies()
    {
        var (client, _) = await StartAsync();

        // Integers are still accepted for enums; responses use names.
        var integers = await PostRawAsync(client, "/api/aster/definitions", ArticleJson("""[{"policyId":"prune","kind":3,"target":1,"outcome":3,"criteria":{"maximumRetainedVersions":3}}]"""));
        Assert.Equal(HttpStatusCode.Created, integers.StatusCode);
        Assert.Contains("\"kind\":\"versionPruning\"", await integers.Content.ReadAsStringAsync());

        async Task AssertBadRequest(HttpResponseMessage response, string code)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(code, (await ReadAsync(response)).GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        await AssertBadRequest(await PostRawAsync(client, "/api/aster/definitions", ArticleJson("""[{"policyId":"p","kind":"bogus","target":"resource","outcome":"retain"}]""")), "request-body-invalid");
        await AssertBadRequest(await PostRawAsync(client, "/api/aster/resources/query", "{\"take\":"), "request-body-invalid");
        await AssertBadRequest(await PostRawAsync(client, "/api/aster/resources", ""), "request-body-required");
        await AssertBadRequest(await PostRawAsync(client, "/api/aster/resources/a-1/activate", "null"), "request-body-required");

        // Routing rejects a non-JSON content type; a request without one reaches the endpoint and gets a problem.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostRawAsync(client, "/api/aster/resources/query", "{}", "text/plain")).StatusCode);
        var noContentType = await client.PostAsync("/api/aster/resources/query", null);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, noContentType.StatusCode);
        Assert.Equal("application/problem+json", noContentType.Content.Headers.ContentType?.MediaType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Escaped IDs in routes (#239)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// IDs with characters a path segment must escape, each over TestServer and over Kestrel. TestServer has no raw
    /// request target, so an ID holding the text <c>%2F</c> only round-trips over Kestrel.
    /// </summary>
    public static TheoryData<string, bool> ReservedCharacterIds()
    {
        var data = new TheoryData<string, bool>();
        foreach (var id in new[] { "a/b", "/lead//trail/", "100%", "%", "a+b", "a b", "a#b", "a?b&c=d", "ünï/cødé ✓", "a%2Fb", "a%252F%2fb", "a%2Fb/c", "%41" })
        {
            if (!id.Contains("%2F", StringComparison.OrdinalIgnoreCase))
                data.Add(id, false);
            data.Add(id, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReservedCharacterIds))]
    public async Task Resources_IdsWithReservedCharacters_RoundTripThroughEscapedPaths(string resourceId, bool loopback)
    {
        var (client, _) = await StartAsync(loopback: loopback);
        await RegisterAsync(client, Article());
        var path = $"/api/aster/resources/{Uri.EscapeDataString(resourceId)}";

        var created = await CreateAsync(client, resourceId, "Hello");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(path, created.Headers.Location?.OriginalString);

        var latest = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal(resourceId, (await ReadAsync(latest)).GetProperty("resourceId").GetString());

        var updated = await client.PutAsJsonAsync(path, new { baseVersion = 1, aspects = Content("Hello again") }, Web);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = await ReadAsync(updated);
        Assert.Equal((resourceId, 2), (second.GetProperty("resourceId").GetString(), second.GetProperty("version").GetInt32()));

        var first = await ReadAsync(await client.GetAsync($"{path}?version=1"));
        Assert.Equal("Hello", first.GetProperty("aspects").GetProperty("Content").GetProperty("Title").GetString());

        async Task<string[]> ActiveChannels(int version)
        {
            var history = await client.GetAsync($"{path}/versions");
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            var body = await ReadAsync(history);
            Assert.Equal(resourceId, body.GetProperty("resourceId").GetString());
            return body.GetProperty("versions").EnumerateArray()
                .Single(entry => entry.GetProperty("version").GetInt32() == version)
                .GetProperty("activeChannels").EnumerateArray().Select(static channel => channel.GetString()!).ToArray();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{path}/activate", new { version = 1, channel = "Published" }, Web)).StatusCode);
        Assert.Equal(new[] { "Published" }, await ActiveChannels(1));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{path}/deactivate", new { version = 1, channel = "Published" }, Web)).StatusCode);
        Assert.Empty(await ActiveChannels(1));

        // The message names the ID as stored, not as escaped.
        var missing = await client.GetAsync($"{path}?version=9");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains($"'{resourceId}'", (await ReadAsync(missing)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Resources_IdsWithReservedCharacters_RoundTripUnderAPathBase()
    {
        var (client, _) = await StartAsync(loopback: true, pathBase: "/host/app");
        await RegisterAsync(client, Article());
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(client, "a%2Fb/c", "Hello")).StatusCode);

        var path = $"/host/app/api/aster/resources/{Uri.EscapeDataString("a%2Fb/c")}";
        Assert.Equal("a%2Fb/c", (await ReadAsync(await client.GetAsync(path))).GetProperty("resourceId").GetString());
        Assert.Equal("a%2Fb/c", (await ReadAsync(await client.GetAsync($"{path}/versions"))).GetProperty("resourceId").GetString());
    }

    [Theory]
    [MemberData(nameof(ReservedCharacterIds))]
    public async Task DefinitionsAndAspects_IdsWithReservedCharacters_RoundTripThroughEscapedPaths(string id, bool loopback)
    {
        var (client, _) = await StartAsync(loopback: loopback);

        var definitionPath = $"/api/aster/definitions/{Uri.EscapeDataString(id)}";
        var registered = await RegisterAsync(client, Article() with { DefinitionId = id });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal(definitionPath, registered.Headers.Location?.OriginalString);
        Assert.Equal(id, (await ReadAsync(await client.GetAsync(definitionPath))).GetProperty("definitionId").GetString());
        Assert.Equal(1, (await ReadAsync(await client.GetAsync($"{definitionPath}?version=1"))).GetProperty("version").GetInt32());
        var schema = await client.GetAsync($"{definitionPath}/schema");
        Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
        Assert.Equal("object", (await ReadAsync(schema)).GetProperty("type").GetString());

        var aspectPath = $"/api/aster/aspects/{Uri.EscapeDataString(id)}";
        var aspect = await client.PostAsJsonAsync("/api/aster/aspects", Seo() with { AspectDefinitionId = id }, Web);
        Assert.Equal(HttpStatusCode.Created, aspect.StatusCode);
        Assert.Equal(aspectPath, aspect.Headers.Location?.OriginalString);
        Assert.Equal(id, (await ReadAsync(await client.GetAsync($"{aspectPath}?version=1"))).GetProperty("aspectDefinitionId").GetString());
        Assert.Equal(1, (await ReadAsync(await client.GetAsync($"{aspectPath}/versions"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{aspectPath}/schema")).StatusCode);
    }

    private sealed class HideSecrets : IAsterResourceAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(AsterResourceAuthorizationContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(context.Operation == AsterResourceOperation.Create || context.ResourceId != "secret");
    }

    private sealed class HeaderTenant : ITenantScopeAccessor
    {
        public TenantScope GetTenantScope(HttpContext httpContext) =>
            TenantScope.FromTenantId(httpContext.Request.Headers["X-Tenant"].ToString());
    }

    private sealed class AlwaysAuthenticated(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test")), "test")));
    }
}
