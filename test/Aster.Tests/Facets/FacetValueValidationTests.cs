using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Services;
using Aster.Tests.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Facets;

/// <summary>
/// US3: both resource managers validate present facet values against declared constraints
/// before lifecycle hooks and before any write, with stable <c>facet-value-*</c> codes.
/// </summary>
public sealed class FacetValueValidationTests
{
    private static FacetDefinition Facet(string id, string type, FacetMetadata? metadata = null, bool isRequired = false) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        IsRequired = isRequired,
        Metadata = metadata,
    };

    private static ResourceDefinition Definition(params (string AspectKey, FacetDefinition[] Facets)[] aspects) => new()
    {
        DefinitionId = "Product",
        Id = Guid.NewGuid().ToString(),
        Version = 0,
        AspectDefinitions = aspects.ToDictionary(
            static entry => entry.AspectKey,
            static entry => new AspectDefinition
            {
                AspectDefinitionId = entry.AspectKey.Split(':')[0],
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = entry.Facets,
            }),
    };

    private static async Task<(ServiceProvider Provider, IResourceManager Manager)> CreateDefaultManagerAsync(
        ResourceDefinition definition)
    {
        var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
        return (provider, provider.GetRequiredService<IResourceManager>());
    }

    private static async Task<InMemoryResourceManager> CreateInMemoryManagerAsync(ResourceDefinition definition)
    {
        var definitionStore = new InMemoryResourceDefinitionStore(NullLogger<InMemoryResourceDefinitionStore>.Instance);
        await definitionStore.RegisterDefinitionAsync(definition);
        return new InMemoryResourceManager(
            new InMemoryResourceStore(),
            definitionStore,
            new GuidIdentityGenerator(),
            NullLogger<InMemoryResourceManager>.Instance);
    }

    private static ResourceDefinition ConstrainedStringDefinition() => Definition(
        ("TitleAspect", [Facet("Title", "string", new FacetMetadata
        {
            AllowedValues = ["alpha", "beta", "gamma", "ab", "abcde"],
            MinLength = 2,
            MaxLength = 5,
            Pattern = "^[a-z]+$",
        })]));

    private static ResourceDefinition NumericDefinition() => Definition(
        ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata { Minimum = 1m, Maximum = 100m })]),
        ("StockAspect", [Facet("Quantity", "int", new FacetMetadata { Minimum = 0m, Maximum = 10m })]));

    private static Dictionary<string, object> TitleAspects(object value) => new()
    {
        ["TitleAspect"] = new Dictionary<string, object> { ["Title"] = value },
    };

    // ────────────────────────────────────────────────────────────────────────
    // T022 — per-constraint violations on CreateAsync and UpdateAsync
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValueOutsideAllowedValues_FailsNotAllowed()
    {
        var (provider, manager) = await CreateDefaultManagerAsync(ConstrainedStringDefinition());
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = TitleAspects("delta"),
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.NotAllowed, diagnostic.Code);
        Assert.Equal("TitleAspect", diagnostic.AspectKey);
        Assert.Equal("Title", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData("a")]       // shorter than MinLength 2
    [InlineData("abcdef")]  // longer than MaxLength 5
    public async Task CreateAsync_LengthOutOfBounds_FailsLengthOutOfRange(string value)
    {
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MinLength = 2, MaxLength = 5 })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = TitleAspects(value) }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.LengthOutOfRange, diagnostic.Code);
    }

    [Theory]
    [InlineData(0.5)]   // below Minimum 1 (decimal comparison)
    [InlineData(100.5)] // above Maximum 100
    public async Task CreateAsync_DecimalOutOfRange_FailsOutOfRange(double raw)
    {
        var (provider, manager) = await CreateDefaultManagerAsync(NumericDefinition());
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["PriceAspect"] = new Dictionary<string, object> { ["Amount"] = (decimal)raw },
                },
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.OutOfRange, diagnostic.Code);
        Assert.Equal("PriceAspect", diagnostic.AspectKey);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task CreateAsync_IntOutOfRange_FailsOutOfRange(int value)
    {
        var (provider, manager) = await CreateDefaultManagerAsync(NumericDefinition());
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["StockAspect"] = new Dictionary<string, object> { ["Quantity"] = value },
                },
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.OutOfRange, diagnostic.Code);
        Assert.Equal("StockAspect", diagnostic.AspectKey);
    }

    [Fact]
    public async Task CreateAsync_PatternMismatch_FailsPatternMismatch()
    {
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { Pattern = "^[a-z]+$" })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = TitleAspects("UPPER") }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.PatternMismatch, diagnostic.Code);
    }

    [Fact]
    public async Task UpdateAsync_ViolatingValue_FailsWithSameCodes()
    {
        var (provider, manager) = await CreateDefaultManagerAsync(ConstrainedStringDefinition());
        await using var _ = provider;

        var created = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = TitleAspects("alpha"),
        });

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = created.Version,
                AspectUpdates = TitleAspects("delta"),
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.NotAllowed, diagnostic.Code);

        // Nothing was persisted — latest version is still the created one.
        var latest = await manager.GetLatestVersionAsync(created.ResourceId);
        Assert.NotNull(latest);
        Assert.Equal(created.Version, latest.Version);
    }

    [Fact]
    public async Task InMemoryManager_CreateAndUpdate_EnforceSameSemantics()
    {
        var manager = await CreateInMemoryManagerAsync(ConstrainedStringDefinition());

        // Create with violating value fails and persists nothing.
        var createException = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                ResourceId = "product-1",
                InitialAspects = TitleAspects("UPPER"),
            }));
        Assert.Contains(createException.Diagnostics, static d => d.Code == FacetValueDiagnosticCodes.PatternMismatch);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));

        // Valid create, then violating update fails.
        var created = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = "product-1",
            InitialAspects = TitleAspects("alpha"),
        });

        var updateException = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.UpdateAsync("product-1", new UpdateResourceRequest
            {
                BaseVersion = created.Version,
                AspectUpdates = TitleAspects("delta"),
            }));
        Assert.Contains(updateException.Diagnostics, static d => d.Code == FacetValueDiagnosticCodes.NotAllowed);

        var latest = await manager.GetLatestVersionAsync("product-1");
        Assert.NotNull(latest);
        Assert.Equal(1, latest.Version);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T023 — success paths and edge cases
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ConformingValues_SaveExactlyAsToday()
    {
        var (provider, manager) = await CreateDefaultManagerAsync(ConstrainedStringDefinition());
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = TitleAspects("alpha"),
        });

        Assert.Equal(1, resource.Version);
        var aspect = Assert.IsType<Dictionary<string, object>>(resource.Aspects["TitleAspect"]);
        Assert.Equal("alpha", aspect["Title"]);
    }

    [Theory]
    [InlineData("ab")]    // length equal to MinLength
    [InlineData("abcde")] // length equal to MaxLength
    public async Task CreateAsync_BoundaryLengths_Pass(string value)
    {
        var (provider, manager) = await CreateDefaultManagerAsync(ConstrainedStringDefinition());
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = TitleAspects(value),
        });

        Assert.Equal(1, resource.Version);
    }

    [Theory]
    [InlineData(1)]   // equal to Minimum
    [InlineData(100)] // equal to Maximum
    public async Task CreateAsync_BoundaryNumericValues_Pass(int value)
    {
        var (provider, manager) = await CreateDefaultManagerAsync(NumericDefinition());
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["PriceAspect"] = new Dictionary<string, object> { ["Amount"] = (decimal)value },
            },
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_EqualMinMaxBounds_AcceptExactValue()
    {
        var definition = Definition(
            ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata { Minimum = 5m, Maximum = 5m })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["PriceAspect"] = new Dictionary<string, object> { ["Amount"] = 5m },
            },
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_ValueAllowedButViolatingPattern_StillFails()
    {
        // "AB" is in the allowed list but violates the lowercase pattern —
        // all declared constraints are enforced together.
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata
            {
                AllowedValues = ["AB", "cd"],
                Pattern = "^[a-z]+$",
            })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest { InitialAspects = TitleAspects("AB") }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.PatternMismatch, diagnostic.Code);
    }

    [Fact]
    public async Task CreateAsync_AbsentValue_SkipsConstraintsWithIsRequiredSemanticsUnchanged()
    {
        // Constraint-bearing facet, but the aspect payload omits the value entirely,
        // and IsRequired stays governed by the existing flag (no new rejection).
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MinLength = 2 }, isRequired: true)]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["TitleAspect"] = new Dictionary<string, object>(),
            },
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_NullValue_SkipsConstraints()
    {
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MinLength = 2 })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["TitleAspect"] = new Dictionary<string, object?> { ["Title"] = null }!,
            },
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_FacetWithoutMetadata_SavesExactlyAsToday()
    {
        var definition = Definition(("TitleAspect", [Facet("Title", "string")]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = TitleAspects("Anything Goes — no constraints!"),
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_DisplayOnlyMetadata_SavesExactlyAsToday()
    {
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata
            {
                Label = "Title",
                Description = "Display only.",
                DisplayOrder = 1,
                UiHint = "text",
            })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = TitleAspects("ANY value at all"),
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_UnnormalizableValue_SkipsChecksAndSavesAsToday()
    {
        // An int value on a string facet cannot be normalized to the declared type —
        // constraint checks are skipped and the save proceeds exactly as today.
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MinLength = 100 })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = TitleAspects(42),
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task CreateAsync_DefaultValueNeverInjectedIntoSavedVersions()
    {
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { DefaultValue = "prefill" })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["TitleAspect"] = new Dictionary<string, object>(),
            },
        });

        var aspect = Assert.IsType<Dictionary<string, object>>(resource.Aspects["TitleAspect"]);
        Assert.False(aspect.ContainsKey("Title"));
    }

    // ────────────────────────────────────────────────────────────────────────
    // T024 — aggregation, ordering, atomicity, payload shapes
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_MultipleViolationsAcrossAspects_ReportsAllOrderedByAspectFacetCode()
    {
        var definition = Definition(
            ("ZetaAspect", [Facet("Name", "string", new FacetMetadata { MaxLength = 3, Pattern = "^[a-z]+$" })]),
            ("AlphaAspect", [Facet("Amount", "decimal", new FacetMetadata { Minimum = 10m })]));
        var (provider, manager) = await CreateDefaultManagerAsync(definition);
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["ZetaAspect"] = new Dictionary<string, object> { ["Name"] = "TOOLONG" },
                    ["AlphaAspect"] = new Dictionary<string, object> { ["Amount"] = 1m },
                },
            }));

        Assert.Equal(3, exception.Diagnostics.Count);

        Assert.Equal("AlphaAspect", exception.Diagnostics[0].AspectKey);
        Assert.Equal("Amount", exception.Diagnostics[0].FacetDefinitionId);
        Assert.Equal(FacetValueDiagnosticCodes.OutOfRange, exception.Diagnostics[0].Code);

        Assert.Equal("ZetaAspect", exception.Diagnostics[1].AspectKey);
        Assert.Equal("Name", exception.Diagnostics[1].FacetDefinitionId);
        Assert.Equal(FacetValueDiagnosticCodes.LengthOutOfRange, exception.Diagnostics[1].Code);

        Assert.Equal("ZetaAspect", exception.Diagnostics[2].AspectKey);
        Assert.Equal("Name", exception.Diagnostics[2].FacetDefinitionId);
        Assert.Equal(FacetValueDiagnosticCodes.PatternMismatch, exception.Diagnostics[2].Code);

        Assert.All(exception.Diagnostics, static diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));
    }

    [Fact]
    public async Task CreateAsync_ViolatingValues_NothingPersistedAndNoBeforeSaveHookRan()
    {
        var provider = LifecycleHookTestFixtures.BuildServices(typeof(FirstRecordingHook));
        await using var _ = provider;
        var recorder = provider.GetRequiredService<LifecycleHookRecorder>();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();

        await definitionStore.RegisterDefinitionAsync(ConstrainedStringDefinition());

        await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                ResourceId = "product-1",
                InitialAspects = TitleAspects("NOT-ALLOWED"),
            }));

        Assert.Empty(recorder.Events);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
    }

    [Fact]
    public async Task CreateAsync_JsonElementAspectPayload_ViolationsDetected()
    {
        var (provider, manager) = await CreateDefaultManagerAsync(ConstrainedStringDefinition());
        await using var _ = provider;

        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["Title"] = "delta" });

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object> { ["TitleAspect"] = payload },
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.NotAllowed, diagnostic.Code);
    }

    private sealed class TitlePayload
    {
        public string? Title { get; init; }
    }

    [Fact]
    public async Task CreateAsync_TypedPocoAspectPayload_ViolationsDetected()
    {
        var (provider, manager) = await CreateDefaultManagerAsync(ConstrainedStringDefinition());
        await using var _ = provider;

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["TitleAspect"] = new TitlePayload { Title = "delta" },
                },
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.NotAllowed, diagnostic.Code);
    }
}
