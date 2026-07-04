using Aster.Core.Exceptions;
using Aster.Core.InMemory;
using Aster.Core.Models.Definitions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Facets;

/// <summary>
/// US2: both definition stores reject invalid metadata combinations at registration with
/// stable <c>facet-metadata-*</c> diagnostic codes, deterministically ordered.
/// </summary>
public sealed class FacetMetadataValidationTests
{
    private static InMemoryResourceDefinitionStore CreateStore() =>
        new(NullLogger<InMemoryResourceDefinitionStore>.Instance);

    private static FacetDefinition Facet(string id, string type, FacetMetadata metadata) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
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

    private static async Task<FacetMetadataValidationException> AssertRejectedAsync(ResourceDefinition definition)
    {
        var store = CreateStore();
        return await Assert.ThrowsAsync<FacetMetadataValidationException>(
            async () => await store.RegisterDefinitionAsync(definition));
    }

    // ────────────────────────────────────────────────────────────────────────
    // T013 — range-invalid and constraint-inapplicable
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_MinLengthGreaterThanMaxLength_FailsRangeInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MinLength = 5, MaxLength = 2 })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.RangeInvalid, diagnostic.Code);
        Assert.Equal("TitleAspect", diagnostic.AspectKey);
        Assert.Equal("Title", diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -2)]
    public async Task Register_NegativeLengthBound_FailsRangeInvalid(int? minLength, int? maxLength)
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MinLength = minLength, MaxLength = maxLength })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.RangeInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_MinimumGreaterThanMaximum_FailsRangeInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata { Minimum = 10m, Maximum = 1m })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.RangeInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_NonWholeBoundOnIntFacet_FailsRangeInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("StockAspect", [Facet("Quantity", "int", new FacetMetadata { Minimum = 1.5m })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.RangeInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_LengthBoundsOnNonStringFacet_FailsConstraintInapplicable()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("StockAspect", [Facet("Quantity", "int", new FacetMetadata { MinLength = 1, MaxLength = 5 })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code);
    }

    [Fact]
    public async Task Register_PatternOnNonStringFacet_FailsConstraintInapplicable()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata { Pattern = "^[0-9]+$" })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code);
    }

    [Fact]
    public async Task Register_NumericRangeOnStringFacet_FailsConstraintInapplicable()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { Minimum = 0m })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code);
    }

    [Fact]
    public async Task Register_NumericRangeOnDatetimeFacet_FailsConstraintInapplicable()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("ScheduleAspect", [Facet("PublishAt", "datetime", new FacetMetadata { Maximum = 100m })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code);
    }

    [Fact]
    public async Task Register_AllowedValuesOnBoolFacet_FailsConstraintInapplicable()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("FlagsAspect", [Facet("IsFeatured", "bool", new FacetMetadata { AllowedValues = [true] })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T014 — allowed values
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_EmptyAllowedValues_FailsAllowedValuesEmpty()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { AllowedValues = [] })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.AllowedValuesEmpty, diagnostic.Code);
    }

    [Fact]
    public async Task Register_NumericDuplicatesAfterNormalization_FailsAllowedValuesDuplicate()
    {
        // 1 (int) and 1.0m (decimal) normalize to the same decimal value on a decimal facet.
        var exception = await AssertRejectedAsync(Definition(
            ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata { AllowedValues = [1, 1.0m] })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.AllowedValuesDuplicate, diagnostic.Code);
    }

    [Fact]
    public async Task Register_DatetimeDuplicateSpellings_FailsAllowedValuesDuplicate()
    {
        // Two ISO 8601 spellings of the same instant.
        var exception = await AssertRejectedAsync(Definition(
            ("ScheduleAspect", [Facet("PublishAt", "datetime", new FacetMetadata
            {
                AllowedValues = ["2026-01-01T00:00:00Z", "2026-01-01T00:00:00+00:00"],
            })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.AllowedValuesDuplicate, diagnostic.Code);
    }

    [Fact]
    public async Task Register_AllowedValueNotNormalizableToDeclaredType_FailsTypeMismatch()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { AllowedValues = ["ok", 5] })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.AllowedValueTypeMismatch, diagnostic.Code);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T015 — default value and pattern
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_DefaultValueViolatingType_FailsDefaultValueInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { DefaultValue = 5 })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.DefaultValueInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_DefaultValueOutsideAllowedValues_FailsDefaultValueInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata
            {
                AllowedValues = ["a", "b"],
                DefaultValue = "c",
            })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.DefaultValueInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_DefaultValueViolatingLength_FailsDefaultValueInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { MaxLength = 3, DefaultValue = "toolong" })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.DefaultValueInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_DefaultValueViolatingRange_FailsDefaultValueInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata { Minimum = 0m, Maximum = 10m, DefaultValue = 11m })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.DefaultValueInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_DefaultValueViolatingPattern_FailsDefaultValueInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { Pattern = "^[a-z]+$", DefaultValue = "UPPER" })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.DefaultValueInvalid, diagnostic.Code);
    }

    [Fact]
    public async Task Register_MalformedPattern_FailsPatternInvalid()
    {
        var exception = await AssertRejectedAsync(Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata { Pattern = "[unclosed" })])));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetMetadataDiagnosticCodes.PatternInvalid, diagnostic.Code);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T016 — aggregation, ordering, determinism, success, atomicity
    // ────────────────────────────────────────────────────────────────────────

    private static ResourceDefinition MultiViolationDefinition() => Definition(
        ("ZetaAspect", [
            Facet("Beta", "string", new FacetMetadata { MinLength = 9, MaxLength = 1, Pattern = "[bad" }),
            Facet("Alpha", "int", new FacetMetadata { Pattern = "^x$" }),
        ]),
        ("AlphaAspect", [
            Facet("Title", "string", new FacetMetadata { AllowedValues = [] }),
        ]));

    [Fact]
    public async Task Register_MultipleViolationsAcrossAspects_ReportsAllOrderedByAspectFacetCode()
    {
        var exception = await AssertRejectedAsync(MultiViolationDefinition());

        Assert.Equal(4, exception.Diagnostics.Count);

        // Ordered by aspect key → facet id → code (ordinal).
        Assert.Equal("AlphaAspect", exception.Diagnostics[0].AspectKey);
        Assert.Equal("Title", exception.Diagnostics[0].FacetDefinitionId);
        Assert.Equal(FacetMetadataDiagnosticCodes.AllowedValuesEmpty, exception.Diagnostics[0].Code);

        Assert.Equal("ZetaAspect", exception.Diagnostics[1].AspectKey);
        Assert.Equal("Alpha", exception.Diagnostics[1].FacetDefinitionId);
        Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, exception.Diagnostics[1].Code);

        Assert.Equal("ZetaAspect", exception.Diagnostics[2].AspectKey);
        Assert.Equal("Beta", exception.Diagnostics[2].FacetDefinitionId);
        Assert.Equal(FacetMetadataDiagnosticCodes.PatternInvalid, exception.Diagnostics[2].Code);

        Assert.Equal("ZetaAspect", exception.Diagnostics[3].AspectKey);
        Assert.Equal("Beta", exception.Diagnostics[3].FacetDefinitionId);
        Assert.Equal(FacetMetadataDiagnosticCodes.RangeInvalid, exception.Diagnostics[3].Code);

        // Every diagnostic carries a message.
        Assert.All(exception.Diagnostics, static diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));
    }

    [Fact]
    public async Task Register_RepeatedValidation_YieldsIdenticalDiagnostics()
    {
        var first = await AssertRejectedAsync(MultiViolationDefinition());
        var second = await AssertRejectedAsync(MultiViolationDefinition());

        Assert.Equal(first.Diagnostics.Count, second.Diagnostics.Count);
        for (var i = 0; i < first.Diagnostics.Count; i++)
        {
            Assert.Equal(first.Diagnostics[i].Code, second.Diagnostics[i].Code);
            Assert.Equal(first.Diagnostics[i].AspectKey, second.Diagnostics[i].AspectKey);
            Assert.Equal(first.Diagnostics[i].FacetDefinitionId, second.Diagnostics[i].FacetDefinitionId);
            Assert.Equal(first.Diagnostics[i].Message, second.Diagnostics[i].Message);
        }
    }

    [Fact]
    public async Task Register_FullyValidMetadata_Succeeds()
    {
        var store = CreateStore();
        var definition = Definition(
            ("TitleAspect", [Facet("Title", "string", new FacetMetadata
            {
                Label = "Title",
                Description = "The product title.",
                DisplayOrder = 1,
                AllowedValues = ["alpha", "beta"],
                MinLength = 2,
                MaxLength = 10,
                Pattern = "^[a-z]+$",
                DefaultValue = "alpha",
                UiHint = "text",
            })]),
            ("PriceAspect", [Facet("Amount", "decimal", new FacetMetadata
            {
                Minimum = 5m,
                Maximum = 5m, // equal bounds are valid and accept exactly that value
                DefaultValue = 5m,
            })]));

        await store.RegisterDefinitionAsync(definition);
        var result = await store.GetDefinitionAsync("Product");

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
    }

    [Fact]
    public async Task Register_InvalidMetadata_LeavesStoreUnchanged()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<FacetMetadataValidationException>(
            async () => await store.RegisterDefinitionAsync(MultiViolationDefinition()));

        Assert.Null(await store.GetDefinitionAsync("Product"));
    }
}
