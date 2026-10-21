using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
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
    private readonly List<string> databases = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
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
        Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateBuilder();
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
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAster("/api/aster", configure);
        await app.StartAsync();
        apps.Add(app);

        return (app.GetTestClient(), app.Services);
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

        var tooLarge = await client.PostAsJsonAsync("/api/aster/resources/query", new { take = 501 }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
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

    [Fact]
    public async Task EveryEndpoint_CarriesOpenApiMetadata()
    {
        var (_, services) = await StartAsync();
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/aster", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(17, endpoints.Count);
        Assert.All(endpoints, static endpoint =>
        {
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary), endpoint.DisplayName);
            Assert.NotEmpty(endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags ?? []);
        });
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
