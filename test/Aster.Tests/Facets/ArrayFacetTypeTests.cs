using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
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
/// #82: array facet types (<c>T[]</c>, ADR 0002 §1, §5, §7) register, validate element by element
/// and as a collection (MinItems, MaxItems, UniqueItems) on save, and round-trip through storage
/// and portability, identically for the in-memory and SQLite JSON providers.
/// </summary>
public sealed class ArrayFacetTypeTests : IDisposable
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
        DefinitionId = "Workflow",
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            ["Labels"] = new()
            {
                AspectDefinitionId = "Labels",
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static Dictionary<string, object> Labels(object payload) => new() { ["Labels"] = payload };

    private static Dictionary<string, object> Tags(object? tags) => Labels(new Dictionary<string, object?> { ["Tags"] = tags });

    private static async Task<IResourceManager> RegisterAsync(ServiceProvider provider, ResourceDefinition definition)
    {
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
        return provider.GetRequiredService<IResourceManager>();
    }

    private static async Task<IReadOnlyList<FacetValueDiagnostic>> SaveDiagnosticsAsync(IResourceManager manager, Dictionary<string, object> aspects)
    {
        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = aspects }));
        return exception.Diagnostics;
    }

    private static async Task<IReadOnlyList<FacetMetadataDiagnostic>> RegistrationDiagnosticsAsync(ServiceProvider provider, ResourceDefinition definition)
    {
        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition));
        return exception.Diagnostics;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Registration
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("string[]")]
    [InlineData("int[]")]
    [InlineData("decimal[]")]
    [InlineData("bool[]")]
    [InlineData("datetime[]")]
    [InlineData("guid[]")]
    [InlineData("String[]")]
    [InlineData("int32[]")]
    [InlineData("datetimeoffset[]")]
    public async Task RegisterDefinitionAsync_ArrayOfSupportedElementType_IsAccepted(string type)
    {
        await using var provider = CreateProvider(InMemory);

        await RegisterAsync(provider, Definition(Facet("Values", type)));
    }

    [Theory]
    [InlineData(InMemory, "string[][]")]
    [InlineData(InMemory, "object[]")]
    [InlineData(InMemory, "strnig[]")]
    [InlineData(InMemory, "[]")]
    [InlineData(InMemory, "string []")]
    [InlineData(Sqlite, "string[][]")]
    public async Task RegisterDefinitionAsync_UnsupportedArrayDescriptor_IsRejected(string providerKind, string type)
    {
        await using var provider = CreateProvider(providerKind);

        var diagnostic = Assert.Single(await RegistrationDiagnosticsAsync(provider, Definition(Facet("Values", type))));

        Assert.Equal(FacetMetadataDiagnosticCodes.TypeUnknown, diagnostic.Code);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_ElementConstraintsOnArrays_AreAccepted()
    {
        await using var provider = CreateProvider(InMemory);

        await RegisterAsync(provider, Definition(
            Facet("Tags", "string[]", metadata: new FacetMetadata
            {
                MinLength = 1,
                MaxLength = 20,
                Pattern = "^[a-z-]+$",
                AllowedValues = ["billing", "ops"],
                MinItems = 0,
                MaxItems = 5,
                UniqueItems = true,
                DefaultValue = new[] { "ops" },
            }),
            Facet("Scores", "int[]", metadata: new FacetMetadata { Minimum = 0, Maximum = 10 })));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    [InlineData(3, 2)]
    public async Task RegisterDefinitionAsync_InvalidItemBounds_AreRejected(int? minItems, int? maxItems)
    {
        await using var provider = CreateProvider(InMemory);

        var diagnostic = Assert.Single(await RegistrationDiagnosticsAsync(provider, Definition(
            Facet("Tags", "string[]", metadata: new FacetMetadata { MinItems = minItems, MaxItems = maxItems }))));

        Assert.Equal(FacetMetadataDiagnosticCodes.RangeInvalid, diagnostic.Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RegisterDefinitionAsync_ItemConstraintsOnScalar_AreInapplicable(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);

        var diagnostics = await RegistrationDiagnosticsAsync(provider, Definition(
            Facet("Name", "string", metadata: new FacetMetadata { MinItems = 1 }),
            Facet("Flag", "bool", metadata: new FacetMetadata { UniqueItems = true })));

        Assert.Equal(
            [("Flag", FacetMetadataDiagnosticCodes.ConstraintInapplicable), ("Name", FacetMetadataDiagnosticCodes.ConstraintInapplicable)],
            diagnostics.Select(static diagnostic => (diagnostic.FacetDefinitionId, diagnostic.Code)));
    }

    [Fact]
    public async Task RegisterDefinitionAsync_ElementConstraintsFollowElementTypeMatrix()
    {
        await using var provider = CreateProvider(InMemory);

        var diagnostics = await RegistrationDiagnosticsAsync(provider, Definition(
            Facet("Flags", "bool[]", metadata: new FacetMetadata { AllowedValues = [true] }),
            Facet("Scores", "int[]", metadata: new FacetMetadata { Pattern = "^1$" }),
            Facet("Tags", "string[]", metadata: new FacetMetadata { Minimum = 1 })));

        Assert.All(diagnostics, static diagnostic => Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code));
        Assert.Equal(["Flags", "Scores", "Tags"], diagnostics.Select(static diagnostic => diagnostic.FacetDefinitionId));
    }

    [Theory]
    [InlineData("not an array")]
    [InlineData("too-many")]
    [InlineData("duplicate")]
    [InlineData("element violates pattern")]
    [InlineData("element mismatch")]
    public async Task RegisterDefinitionAsync_InvalidArrayDefault_IsRejected(string scenario)
    {
        await using var provider = CreateProvider(InMemory);
        object defaultValue = scenario switch
        {
            "not an array" => "ops",
            "too-many" => new[] { "a", "b", "c" },
            "duplicate" => new[] { "ops", "ops" },
            "element violates pattern" => new[] { "Ops" },
            _ => new object[] { "ops", 3 },
        };

        var diagnostic = Assert.Single(await RegistrationDiagnosticsAsync(provider, Definition(
            Facet("Tags", "string[]", metadata: new FacetMetadata
            {
                Pattern = "^[a-z]+$",
                MaxItems = 2,
                UniqueItems = true,
                DefaultValue = defaultValue,
            }))));

        Assert.Equal(FacetMetadataDiagnosticCodes.DefaultValueInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_ArrayDefaultAfterJsonRoundTrip_IsAccepted()
    {
        await using var provider = CreateProvider(Sqlite);
        var defaultValue = JsonSerializer.Deserialize<JsonElement>("""["ops","billing"]""");

        await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata { DefaultValue = defaultValue, UniqueItems = true })));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save: shape and element types
    // ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, object> ValidArrays => new()
    {
        { InMemory, "string[]", new[] { "billing", "ops" } },
        { Sqlite, "string[]", new List<string> { "billing", "ops" } },
        { InMemory, "int[]", new[] { 1, 2, 3 } },
        { Sqlite, "int[]", new List<long> { 1, 2, 3 } },
        { InMemory, "decimal[]", new[] { 1.5m, 2m } },
        { Sqlite, "decimal[]", new object[] { 1, 2.5m } },
        { InMemory, "bool[]", new[] { true, false } },
        { Sqlite, "datetime[]", new[] { "2026-10-20T00:00:00Z", "2026-11-01T12:00:00+02:00" } },
        { InMemory, "datetime[]", new[] { new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero) } },
        { Sqlite, "guid[]", new[] { Guid.NewGuid(), Guid.NewGuid() } },
        { InMemory, "guid[]", new[] { Guid.NewGuid().ToString("D").ToUpperInvariant() } },
        { Sqlite, "string[]", Array.Empty<string>() },
        { InMemory, "string[]", JsonSerializer.Deserialize<JsonElement>("""["a","b"]""") },
    };

    [Theory]
    [MemberData(nameof(ValidArrays))]
    public async Task CreateAsync_ValidArray_IsSavedAndReadBack(string providerKind, string type, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", type)));

        var created = await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Tags(value) });

        var latest = await manager.GetLatestVersionAsync(created.ResourceId);
        Assert.NotNull(latest);

        // An update that carries the stored aspect forward revalidates the read-back value.
        await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = latest.Version,
            AspectUpdates = new Dictionary<string, object> { ["Labels"] = latest.Aspects["Labels"] },
        });
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_ScalarForArrayFacet_IsTypeMismatch(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]")));

        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Tags("billing")));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Null(diagnostic.ElementIndex);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_ArrayForScalarFacet_IsTypeMismatch(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string")));

        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Tags(new[] { "billing" })));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_ObjectForArrayFacet_IsTypeMismatch(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]")));

        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Tags(new Dictionary<string, object> { ["a"] = "b" })));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Null(diagnostic.ElementIndex);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_MismatchedAndNullElements_ReportEachIndexAndPersistNothing(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Scores", "int[]", metadata: new FacetMetadata { Maximum = 1 })));

        var diagnostics = await SaveDiagnosticsAsync(manager, Labels(new Dictionary<string, object>
        {
            ["Scores"] = new object?[] { 1, "two", null, 3.5m },
        }));

        // Constraints are skipped while any element is mistyped, so Maximum does not fire for 3.5.
        Assert.Equal(
            [(FacetValueDiagnosticCodes.TypeMismatch, (int?)1), (FacetValueDiagnosticCodes.TypeMismatch, 2), (FacetValueDiagnosticCodes.TypeMismatch, 3)],
            diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.ElementIndex)));
        Assert.Contains("Element [1]", diagnostics[0].Message);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_JsonArrayWithMismatchedElement_IsRejected(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Owners", "guid[]")));
        var payload = JsonSerializer.Deserialize<JsonElement>($$"""{"Owners":["{{Guid.NewGuid()}}","not-a-guid",{"id":1}]}""");

        var diagnostics = await SaveDiagnosticsAsync(manager, Labels(payload));

        Assert.Equal([1, 2], diagnostics.Select(static diagnostic => diagnostic.ElementIndex!.Value));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save: required and collection-level constraints
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory, "missing")]
    [InlineData(InMemory, "null")]
    [InlineData(InMemory, "empty")]
    [InlineData(Sqlite, "missing")]
    [InlineData(Sqlite, "empty")]
    public async Task CreateAsync_RequiredArrayMissingNullOrEmpty_IsRequiredViolation(string providerKind, string shape)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", isRequired: true), Facet("Note", "string")));
        var facets = new Dictionary<string, object?> { ["Note"] = "x" };
        if (shape == "null")
            facets["Tags"] = null;
        else if (shape == "empty")
            facets["Tags"] = Array.Empty<string>();

        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Labels(facets)));

        Assert.Equal(FacetValueDiagnosticCodes.Required, diagnostic.Code);
    }

    [Fact]
    public async Task CreateAsync_OversizedArray_SkipsPerElementChecks()
    {
        await using var provider = CreateProvider(InMemory);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata
        {
            MaxItems = 2,
            UniqueItems = true,
            Pattern = "^[a-z]+$",
        })));

        // Over MaxItems, with duplicates and pattern violations: only the count is reported, so element
        // work (patterns) stays bounded by MaxItems.
        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Tags(new[] { "A1", "A1", "B2", "C3" })));

        Assert.Equal(FacetValueDiagnosticCodes.ItemsOutOfRange, diagnostic.Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_OptionalEmptyArray_IsAccepted(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata { MaxItems = 2, UniqueItems = true })));

        await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Tags(Array.Empty<string>()) });
    }

    [Theory]
    [InlineData(InMemory, 0)]
    [InlineData(InMemory, 4)]
    [InlineData(Sqlite, 0)]
    [InlineData(Sqlite, 4)]
    public async Task CreateAsync_ElementCountOutsideBounds_IsItemsOutOfRange(string providerKind, int count)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata { MinItems = 1, MaxItems = 3 })));

        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Tags(Enumerable.Range(0, count).Select(static i => $"t{i}").ToArray())));

        Assert.Equal(FacetValueDiagnosticCodes.ItemsOutOfRange, diagnostic.Code);
        Assert.Null(diagnostic.ElementIndex);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_ElementCountWithinBounds_IsAccepted(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata { MinItems = 1, MaxItems = 3 })));

        await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Tags(new[] { "a", "b", "c" }) });
    }

    public static TheoryData<string, string, object> DuplicateArrays => new()
    {
        { InMemory, "string[]", new[] { "ops", "billing", "ops" } },
        { Sqlite, "int[]", new object[] { 1, 2, 1L } },
        { InMemory, "decimal[]", new object[] { 1.5m, 1.50m } },
        { Sqlite, "guid[]", new[] { "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "6F9619FF-8B86-D011-B42D-00CF4FC964FF" } },
        { InMemory, "datetime[]", new[] { "2026-10-20T02:00:00+02:00", "2026-10-20T00:00:00Z" } },
    };

    [Theory]
    [MemberData(nameof(DuplicateArrays))]
    public async Task CreateAsync_UniqueItemsWithDuplicatesAfterNormalization_IsRejected(string providerKind, string type, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", type, metadata: new FacetMetadata { UniqueItems = true })));

        var diagnostic = Assert.Single(await SaveDiagnosticsAsync(manager, Tags(value)));

        Assert.Equal(FacetValueDiagnosticCodes.ItemsNotUnique, diagnostic.Code);
        Assert.NotNull(diagnostic.ElementIndex);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_StringUniquenessIsOrdinal(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata { UniqueItems = true })));

        await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Tags(new[] { "Ops", "ops" }) });
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_DuplicatesWithoutUniqueItems_AreAccepted(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]")));

        await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Tags(new[] { "ops", "ops" }) });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save: per-element constraints
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_ElementConstraintViolations_IdentifyEachElement(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(
            Facet("Tags", "string[]", metadata: new FacetMetadata
            {
                AllowedValues = ["billing", "ops", "Ops", "toolong"],
                MaxLength = 5,
                Pattern = "^[a-z]+$",
            }),
            Facet("Scores", "decimal[]", metadata: new FacetMetadata { Minimum = 0, Maximum = 10 })));

        var diagnostics = await SaveDiagnosticsAsync(manager, Labels(new Dictionary<string, object>
        {
            ["Tags"] = new[] { "ops", "sales", "Ops", "toolong" },
            ["Scores"] = new[] { 5m, -1m, 10.5m },
        }));

        Assert.Equal(
            [
                ("Scores", FacetValueDiagnosticCodes.OutOfRange, (int?)1),
                ("Scores", FacetValueDiagnosticCodes.OutOfRange, 2),
                ("Tags", FacetValueDiagnosticCodes.LengthOutOfRange, 3),
                ("Tags", FacetValueDiagnosticCodes.NotAllowed, 1),
                ("Tags", FacetValueDiagnosticCodes.PatternMismatch, 2),
            ],
            diagnostics.Select(static diagnostic => (diagnostic.FacetDefinitionId, diagnostic.Code, diagnostic.ElementIndex)));
        Assert.StartsWith("Element [1]:", diagnostics[0].Message);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_GuidAllowedValuesCompareCaseInsensitivelyPerElement(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var allowed = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
        var manager = await RegisterAsync(provider, Definition(Facet("Owners", "guid[]", metadata: new FacetMetadata { AllowedValues = [allowed.ToString()] })));

        await manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            InitialAspects = Labels(new Dictionary<string, object> { ["Owners"] = new[] { allowed.ToString("D").ToUpperInvariant() } }),
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Updates, typed aspects, portability, regressions
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UpdateAsync_ArrayViolation_IsRejectedAndNothingPersisted(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(Facet("Tags", "string[]", metadata: new FacetMetadata { MaxItems = 1 })));
        var created = await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Tags(new[] { "ops" }) });

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = created.Version,
                AspectUpdates = Tags(new[] { "ops", "billing" }),
            }));

        Assert.Equal(FacetValueDiagnosticCodes.ItemsOutOfRange, Assert.Single(exception.Diagnostics).Code);
        Assert.Single(await manager.GetVersionsAsync(created.ResourceId));
    }

    private sealed class LabelsAspect
    {
        public required IReadOnlyList<string> Tags { get; init; }

        public int[] Scores { get; init; } = [];
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task TypedAspect_CollectionPropertiesDeriveArrayFacetsAndValidate(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Workflow")
            .WithAspect<LabelsAspect>()
            .WithFacetMetadata("Tags", new FacetMetadata { UniqueItems = true })
            .Build();
        var manager = await RegisterAsync(provider, definition);

        var created = await manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["LabelsAspect"] = new LabelsAspect { Tags = ["ops"], Scores = [1, 2] } },
        });
        Assert.Equal(1, created.Version);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Workflow", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object> { ["LabelsAspect"] = new LabelsAspect { Tags = [] } },
            }));
        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(exception.Diagnostics).Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Portability_ArrayValuesRoundTripAndRevalidate(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        var definition = Definition(
            Facet("Tags", "string[]", isRequired: true, metadata: new FacetMetadata { UniqueItems = true, MaxItems = 3 }),
            Facet("Owners", "guid[]"),
            Facet("Due", "datetime[]"));
        var sourceManager = await RegisterAsync(source, definition);
        var created = await sourceManager.CreateAsync("Workflow", new CreateResourceRequest
        {
            InitialAspects = Labels(new Dictionary<string, object>
            {
                ["Tags"] = new[] { "billing", "ops" },
                ["Owners"] = new[] { Guid.NewGuid() },
                ["Due"] = new[] { DateTime.UtcNow },
            }),
        });

        var export = await source.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [created.ResourceId],
        });
        Assert.NotNull(export.Snapshot);

        await using var target = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        var import = await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(export.Snapshot);
        Assert.Equal(PortableImportStatus.Imported, import.Status);

        var targetManager = target.GetRequiredService<IResourceManager>();
        var imported = await targetManager.GetLatestVersionAsync(created.ResourceId);
        Assert.NotNull(imported);
        await targetManager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = imported.Version,
            AspectUpdates = new Dictionary<string, object> { ["Labels"] = imported.Aspects["Labels"] },
        });
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ScalarFacets_AreUnaffected(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, Definition(
            Facet("Name", "string", isRequired: true, metadata: new FacetMetadata { MaxLength = 3 }),
            Facet("Count", "int")));

        var diagnostics = await SaveDiagnosticsAsync(manager, Labels(new Dictionary<string, object> { ["Name"] = "long", ["Count"] = "x" }));

        Assert.Equal(
            [("Count", FacetValueDiagnosticCodes.TypeMismatch, (int?)null), ("Name", FacetValueDiagnosticCodes.LengthOutOfRange, null)],
            diagnostics.Select(static diagnostic => (diagnostic.FacetDefinitionId, diagnostic.Code, diagnostic.ElementIndex)));
    }
}
