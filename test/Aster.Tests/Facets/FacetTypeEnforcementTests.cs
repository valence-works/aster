using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// Feature 039: unknown facet types are rejected at registration, and declared types and
/// IsRequired are enforced on save, identically for the in-memory and SQLite JSON providers.
/// Portability import and schema upgrades keep accepting historical data.
/// </summary>
public sealed class FacetTypeEnforcementTests : IDisposable
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
        DefinitionId = "Product",
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            ["Details"] = new()
            {
                AspectDefinitionId = "Details",
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static Dictionary<string, object> Details(object payload) => new() { ["Details"] = payload };

    private static async Task<IResourceManager> RegisterAsync(ServiceProvider provider, ResourceDefinition definition)
    {
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
        return provider.GetRequiredService<IResourceManager>();
    }

    private static async Task<FacetValueDiagnostic> AssertSingleSaveDiagnosticAsync(Func<Task> save)
    {
        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(save);
        return Assert.Single(exception.Diagnostics);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // US1: unknown types at registration
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RegisterDefinitionAsync_UnknownType_IsRejected(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await provider.GetRequiredService<IResourceDefinitionStore>()
                .RegisterDefinitionAsync(Definition(Facet("Title", "strnig"))));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.TypeUnknown, diagnostic.Code);
        Assert.Equal("Details", diagnostic.AspectKey);
        Assert.Equal("Title", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("String")]
    [InlineData("int")]
    [InlineData("int32")]
    [InlineData("int64")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("boolean")]
    [InlineData("datetime")]
    [InlineData("datetimeoffset")]
    public async Task RegisterDefinitionAsync_SupportedTypeOrAlias_IsAccepted(string type)
    {
        await using var provider = CreateProvider(InMemory);

        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(Definition(Facet("Value", type)));
    }

    [Fact]
    public async Task RegisterDefinitionAsync_UnknownTypeAndInvalidMetadata_ReportsAllDiagnosticsInOrder()
    {
        await using var provider = CreateProvider(InMemory);
        var definition = Definition(
            Facet("Zeta", "uuid"),
            Facet("Alpha", "string", metadata: new FacetMetadata { MinLength = 5, MaxLength = 1 }),
            Facet("Beta", "object", metadata: new FacetMetadata { Pattern = "[" }));

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition));

        Assert.Equal(
            [
                ("Alpha", FacetMetadataDiagnosticCodes.RangeInvalid),
                ("Beta", FacetMetadataDiagnosticCodes.TypeUnknown),
                ("Zeta", FacetMetadataDiagnosticCodes.TypeUnknown),
            ],
            exception.Diagnostics.Select(static diagnostic => (diagnostic.FacetDefinitionId, diagnostic.Code)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // US2: declared types on save
    // ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, object> Mismatches()
    {
        var data = new TheoryData<string, string, object>();
        foreach (var providerKind in new[] { InMemory, Sqlite })
        {
            data.Add(providerKind, "string", 42);
            data.Add(providerKind, "int", "seven");
            data.Add(providerKind, "int", 1.5m);
            data.Add(providerKind, "decimal", "1.5");
            data.Add(providerKind, "bool", "true");
            data.Add(providerKind, "datetime", "not-a-date");
            data.Add(providerKind, "string", new Dictionary<string, object> { ["nested"] = "value" });
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Mismatches))]
    public async Task CreateAsync_ValueNotMatchingDeclaredType_IsRejectedWithoutMetadata(string providerKind, string type, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Value", type)));

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = Details(new Dictionary<string, object> { ["Value"] = value }),
            }));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal("Value", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_MismatchInJsonElementPayload_IsRejected(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("ExpiresAt", "datetime"), Facet("Title", "string")));
        var payload = JsonSerializer.Deserialize<JsonElement>("""{ "ExpiresAt": "2026-10-20T00:00:00Z", "Title": { "text": "x" } }""");

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = Details(payload) }));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal("Title", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData(InMemory, "decimal")]
    [InlineData(InMemory, "int")]
    [InlineData(Sqlite, "decimal")]
    public async Task CreateAsync_JsonNumberOutsideSupportedRange_IsRejected(string providerKind, string type)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Amount", type)));
        var payload = JsonSerializer.Deserialize<JsonElement>("""{ "Amount": 1e400 }""");

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = Details(payload) }));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_MismatchInTypedPayload_IsRejected(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Amount", "decimal"), Facet("Title", "int")));

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = Details(new TypedPayload { Amount = 9.5m, Title = "not a number" }),
            }));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal("Title", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_WellTypedValues_SaveUnchanged(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(
            Facet("Title", "string"),
            Facet("Count", "int"),
            Facet("Amount", "decimal"),
            Facet("Enabled", "bool"),
            Facet("ExpiresAt", "datetime")));

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object>
            {
                ["Title"] = "Gadget",
                ["Count"] = 3,
                ["Amount"] = 7,
                ["Enabled"] = true,
                ["ExpiresAt"] = "2026-10-20T12:00:00+02:00",
            }),
        });

        Assert.Equal(1, resource.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UpdateAsync_ValueNotMatchingDeclaredType_IsRejectedAndNothingPersisted(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Count", "int")));
        var created = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object> { ["Count"] = 1 }),
        });

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = created.Version,
                AspectUpdates = Details(new Dictionary<string, object> { ["Count"] = "two" }),
            }));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Single(await manager.GetVersionsAsync(created.ResourceId));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // US3: required facets
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory, false)]
    [InlineData(InMemory, true)]
    [InlineData(Sqlite, false)]
    [InlineData(Sqlite, true)]
    public async Task CreateAsync_RequiredFacetMissingOrNull_IsRejected(string providerKind, bool explicitNull)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Name", "string", isRequired: true), Facet("Color", "string")));
        var facets = new Dictionary<string, object?> { ["Color"] = "#000000" };
        if (explicitNull)
            facets["Name"] = null;

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = Details(facets) }));

        Assert.Equal(FacetValueDiagnosticCodes.Required, diagnostic.Code);
        Assert.Equal("Name", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_RequiredFacetOnAbsentAspect_IsNotGoverned(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Name", "string", isRequired: true)));

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest());

        Assert.Equal(1, resource.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UpdateAsync_MergedAspectsStillMissingRequiredFacet_IsRejectedUntilProvided(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();

        // Version 1 of the definition has no required facet, so the resource saves without Name.
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string"), Facet("Color", "string")));
        var created = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object> { ["Color"] = "#000000" }),
        });

        // Version 2 makes Name required; an update that does not supply it is rejected.
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string", isRequired: true), Facet("Color", "string")));
        var upgraded = await provider.GetRequiredService<IResourceSchemaVersionService>()
            .UpgradeAsync(created.ResourceId, new ResourceSchemaUpgradeRequest { BaseVersion = created.Version });

        var diagnostic = await AssertSingleSaveDiagnosticAsync(async () =>
            await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = upgraded.Resource!.Version,
                AspectUpdates = Details(new Dictionary<string, object> { ["Color"] = "#ffffff" }),
            }));
        Assert.Equal(FacetValueDiagnosticCodes.Required, diagnostic.Code);

        var fixedVersion = await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = upgraded.Resource!.Version,
            AspectUpdates = Details(new Dictionary<string, object> { ["Name"] = "Black", ["Color"] = "#ffffff" }),
        });
        Assert.Equal(3, fixedVersion.Version);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // US4: history paths stay non-validating
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task SchemaUpgrade_OverVersionViolatingNewRules_Succeeds(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string")));
        var created = await provider.GetRequiredService<IResourceManager>().CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object>()),
        });
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string", isRequired: true)));

        var result = await provider.GetRequiredService<IResourceSchemaVersionService>()
            .UpgradeAsync(created.ResourceId, new ResourceSchemaUpgradeRequest { BaseVersion = created.Version });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Equal(2, result.Resource!.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ImportAsync_SnapshotWithVersionsViolatingNewRules_Imports(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definition = Definition(Facet("Name", "string", isRequired: true), Facet("Count", "int"));
        var snapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions = [definition],
            Resources =
            [
                new Resource
                {
                    ResourceId = "legacy",
                    Id = Guid.NewGuid().ToString(),
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Aspects = Details(new Dictionary<string, object> { ["Count"] = "not a number" }),
                },
            ],
        };

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.NotNull(await provider.GetRequiredService<IResourceManager>().GetLatestVersionAsync("legacy"));
    }

    private sealed class TypedPayload
    {
        public decimal Amount { get; init; }

        public string Title { get; init; } = string.Empty;
    }
}
