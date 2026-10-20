using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// #84: the <c>object</c> facet type (ADR 0002 §1, §5, §7) stores structured JSON objects verbatim,
/// rejects non-object values on save, round-trips through both providers and portability, and is
/// derived from nested types on typed aspects.
/// </summary>
public sealed class ObjectFacetTypeTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static FacetDefinition Facet(string id, string type, bool isRequired = false, FacetMetadata? metadata = null) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        IsRequired = isRequired,
        Metadata = metadata,
    };

    private static ResourceDefinition Definition(params FacetDefinition[] facets) => new()
    {
        DefinitionId = "Agent",
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            ["Model"] = new()
            {
                AspectDefinitionId = "Model",
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static Dictionary<string, object> Settings(object? settings) =>
        new() { ["Model"] = new Dictionary<string, object?> { ["Settings"] = settings } };

    private static async Task<IResourceManager> RegisterAsync(ServiceProvider provider, ResourceDefinition definition)
    {
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
        return provider.GetRequiredService<IResourceManager>();
    }

    private sealed record ModelSettings(string Provider, string Model, decimal Temperature, int MaxTokens);

    // ──────────────────────────────────────────────────────────────────────────
    // Registration
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("object")]
    [InlineData("Object")]
    public async Task RegisterDefinitionAsync_ObjectType_IsAccepted(string type)
    {
        await using var provider = CreateProvider(InMemory);

        await RegisterAsync(provider, Definition(Facet("Settings", type, metadata: new FacetMetadata { Label = "Settings", UiHint = "json" })));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RegisterDefinitionAsync_ObjectArray_IsUnknownType(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await RegisterAsync(provider, Definition(Facet("Settings", "object[]"))));

        Assert.Equal(FacetMetadataDiagnosticCodes.TypeUnknown, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_ValueConstraintsOnObject_AreInapplicable()
    {
        await using var provider = CreateProvider(InMemory);

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await RegisterAsync(provider, Definition(
                Facet("A", "object", metadata: new FacetMetadata { AllowedValues = ["x"] }),
                Facet("B", "object", metadata: new FacetMetadata { DefaultValue = new Dictionary<string, object> { ["k"] = 1 } }),
                Facet("C", "object", metadata: new FacetMetadata { MaxLength = 3, Minimum = 1, MinItems = 1 }))));

        Assert.All(exception.Diagnostics, static diagnostic => Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code));
        Assert.Equal(["A", "B", "C", "C", "C"], exception.Diagnostics.Select(static diagnostic => diagnostic.FacetDefinitionId));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save
    // ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, object> ObjectValues => new()
    {
        { InMemory, new Dictionary<string, object> { ["provider"] = "openai", ["temperature"] = 0.2m } },
        { Sqlite, new Dictionary<string, object> { ["provider"] = "openai", ["nested"] = new Dictionary<string, object> { ["a"] = new[] { 1, 2 } } } },
        { InMemory, new ModelSettings("openai", "gpt", 0.2m, 512) },
        { Sqlite, new ModelSettings("anthropic", "claude", 0.7m, 1024) },
        { InMemory, new { provider = "local" } },
        { Sqlite, JsonSerializer.Deserialize<JsonElement>("""{"provider":"openai","stop":["\n"],"extra":null}""") },
        { InMemory, new Dictionary<string, object>() },
    };

    [Theory]
    [MemberData(nameof(ObjectValues))]
    public async Task CreateAsync_ObjectValue_IsSavedAndRevalidatesAfterReadBack(string providerKind, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Settings", "object", isRequired: true)));

        var created = await manager.CreateAsync("Agent", new CreateResourceRequest { InitialAspects = Settings(value) });
        var latest = await manager.GetLatestVersionAsync(created.ResourceId);
        Assert.NotNull(latest);

        await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = latest.Version,
            AspectUpdates = new Dictionary<string, object> { ["Model"] = latest.Aspects["Model"] },
        });
    }

    public static TheoryData<string, object> NonObjectValues => new()
    {
        { InMemory, "openai" },
        { Sqlite, 42 },
        { InMemory, true },
        { Sqlite, new[] { "a", "b" } },
        { InMemory, new List<object> { new Dictionary<string, object> { ["a"] = 1 } } },
        { Sqlite, DateTime.UtcNow },
        { InMemory, Guid.NewGuid() },
        { Sqlite, TimeSpan.FromMinutes(1) },
        { InMemory, JsonSerializer.Deserialize<JsonElement>("""["a"]""") },
        { Sqlite, JsonSerializer.Deserialize<JsonElement>("\"text\"") },
    };

    [Theory]
    [MemberData(nameof(NonObjectValues))]
    public async Task CreateAsync_NonObjectValue_IsTypeMismatchAndNothingPersisted(string providerKind, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Settings", "object")));

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Agent", new CreateResourceRequest { InitialAspects = Settings(value) }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal("Settings", diagnostic.FacetDefinitionId);
        Assert.Empty(await provider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery { DefinitionId = "Agent" }));
    }

    [Theory]
    [InlineData(InMemory, false)]
    [InlineData(Sqlite, true)]
    public async Task CreateAsync_RequiredObjectMissingOrNull_IsRequiredViolation(string providerKind, bool explicitNull)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Settings", "object", isRequired: true), Facet("Name", "string")));
        var facets = new Dictionary<string, object?> { ["Name"] = "agent" };
        if (explicitNull)
            facets["Settings"] = null;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Agent", new CreateResourceRequest { InitialAspects = new Dictionary<string, object> { ["Model"] = facets } }));

        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(exception.Diagnostics).Code);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Round-trip, query, typed aspects
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ObjectValue_RoundTripsUnchangedThroughStoreAndPortability(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        var sourceManager = await RegisterAsync(source, Definition(Facet("Settings", "object")));
        var original = JsonSerializer.Deserialize<JsonElement>("""{"provider":"openai","temperature":0.25,"stop":["\n","END"],"limits":{"tokens":512,"strict":true},"note":null}""");
        var created = await sourceManager.CreateAsync("Agent", new CreateResourceRequest { InitialAspects = Settings(original) });

        var stored = await sourceManager.GetLatestVersionAsync(created.ResourceId);
        Assert.True(JsonEquals(original, SettingsOf(stored!)));

        var export = await source.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [created.ResourceId],
        });
        await using var target = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        var import = await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(export.Snapshot!);
        Assert.Equal(PortableImportStatus.Imported, import.Status);

        var imported = await target.GetRequiredService<IResourceManager>().GetLatestVersionAsync(created.ResourceId);
        Assert.True(JsonEquals(original, SettingsOf(imported!)));
    }

    private static JsonElement SettingsOf(Resource resource) =>
        JsonSerializer.SerializeToElement(resource.Aspects["Model"]).GetProperty("Settings");

    // JsonElement.DeepEquals is .NET 9+; this suite also runs on .NET 8.
    private static bool JsonEquals(JsonElement left, JsonElement right) => (left.ValueKind, right.ValueKind) switch
    {
        (JsonValueKind.Object, JsonValueKind.Object) =>
            left.EnumerateObject().Count() == right.EnumerateObject().Count()
            && left.EnumerateObject().All(property => right.TryGetProperty(property.Name, out var other) && JsonEquals(property.Value, other)),
        (JsonValueKind.Array, JsonValueKind.Array) =>
            left.GetArrayLength() == right.GetArrayLength()
            && left.EnumerateArray().Zip(right.EnumerateArray()).All(static pair => JsonEquals(pair.First, pair.Second)),
        (JsonValueKind.Number, JsonValueKind.Number) => left.GetDecimal() == right.GetDecimal(),
        (JsonValueKind.String, JsonValueKind.String) => left.GetString() == right.GetString(),
        var (leftKind, rightKind) => leftKind == rightKind,
    };

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Exists_MatchesObjectFacets(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Settings", "object"), Facet("Name", "string")));
        var withSettings = await manager.CreateAsync("Agent", new CreateResourceRequest
        {
            InitialAspects = Settings(new Dictionary<string, object> { ["provider"] = "openai" }),
        });
        await manager.CreateAsync("Agent", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Model"] = new Dictionary<string, object> { ["Name"] = "bare" } },
        });

        var results = await provider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            DefinitionId = "Agent",
            Filter = new FacetValueFilter("Model", "Settings", true, ComparisonOperator.Exists),
        });

        Assert.Equal(withSettings.ResourceId, Assert.Single(results).ResourceId);
    }

    private sealed class AgentModel
    {
        public required string Name { get; init; }

        public required ModelSettings Settings { get; init; }

        public Dictionary<string, string> Headers { get; init; } = [];
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task TypedAspect_NestedTypesDeriveObjectFacetsAndValidate(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definition = new ResourceDefinitionBuilder().WithDefinitionId("Agent").WithAspect<AgentModel>().Build();
        Assert.Equal(
            [("Name", "string", true), ("Settings", "object", true), ("Headers", "object", false)],
            definition.AspectDefinitions["AgentModel"].FacetDefinitions.Select(static facet => (facet.FacetDefinitionId, facet.Type, facet.IsRequired)));
        var manager = await RegisterAsync(provider, definition);

        var created = await manager.CreateAsync("Agent", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["AgentModel"] = new AgentModel { Name = "summarizer", Settings = new ModelSettings("openai", "gpt", 0.1m, 256) },
            },
        });
        Assert.Equal(1, created.Version);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Agent", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["AgentModel"] = new Dictionary<string, object> { ["Name"] = "x", ["Settings"] = "gpt" },
                },
            }));
        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, Assert.Single(exception.Diagnostics).Code);
    }
}
