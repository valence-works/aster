using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

/// <summary>
/// US4: facet metadata persists losslessly through each persistent provider, and every provider
/// produces identical validation outcomes, diagnostic codes, and
/// ordering for every stable code (SC-006, FR-020).
/// </summary>
public abstract class FacetMetadataStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    private static ServiceProvider CreateInMemoryProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

    public void Dispose() => Database.Dispose();

    private sealed class TitleAspect
    {
        public string String { get; init; } = string.Empty;
    }

    private sealed class CountAspect
    {
        public int Int32 { get; init; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T032 — persistence
    // ──────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task RegisterDefinitionAsync_FullMetadata_ReadsBackEveryPropertyThroughJsonPayload()
    {
        // Arrange
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .WithFacetMetadata(new FacetMetadata
            {
                Label = "Title",
                Description = "The product title.",
                DisplayOrder = 3,
                DefaultValue = "alpha",
                AllowedValues = ["alpha", "beta"],
                MinLength = 2,
                MaxLength = 16,
                Pattern = "^[a-z]+$",
                UiHint = "text",
            })
            .Build();

        // Act — register with one store instance, read back with a fresh one (forces the
        // value through the persisted JSON payload).
        await using (var writer = Database.CreateProvider())
            await writer.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
        await using var reader = Database.CreateProvider();
        var result = await reader.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("Product");

        // Assert
        Assert.NotNull(result);
        var facet = Assert.Single(result.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.NotNull(facet.Metadata);
        Assert.Equal("Title", facet.Metadata.Label);
        Assert.Equal("The product title.", facet.Metadata.Description);
        Assert.Equal(3, facet.Metadata.DisplayOrder);
        Assert.Equal("alpha", GetString(facet.Metadata.DefaultValue));
        Assert.NotNull(facet.Metadata.AllowedValues);
        Assert.Equal(
            (IEnumerable<string>)["alpha", "beta"],
            facet.Metadata.AllowedValues.Select(GetString));
        Assert.Equal(2, facet.Metadata.MinLength);
        Assert.Equal(16, facet.Metadata.MaxLength);
        Assert.Equal("^[a-z]+$", facet.Metadata.Pattern);
        Assert.Equal("text", facet.Metadata.UiHint);
        Assert.Null(facet.Metadata.Minimum);
        Assert.Null(facet.Metadata.Maximum);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T033 — provider-parity matrix
    // ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> MetadataRejectionCases => new()
    {
        { FacetMetadataDiagnosticCodes.RangeInvalid, "range-invalid" },
        { FacetMetadataDiagnosticCodes.ConstraintInapplicable, "constraint-inapplicable" },
        { FacetMetadataDiagnosticCodes.AllowedValuesEmpty, "allowed-values-empty" },
        { FacetMetadataDiagnosticCodes.AllowedValuesDuplicate, "allowed-values-duplicate" },
        { FacetMetadataDiagnosticCodes.AllowedValueTypeMismatch, "allowed-value-type-mismatch" },
        { FacetMetadataDiagnosticCodes.DefaultValueInvalid, "default-value-invalid" },
        { FacetMetadataDiagnosticCodes.PatternInvalid, "pattern-invalid" },
    };

    [SkippableTheory]
    [MemberData(nameof(MetadataRejectionCases))]
    public async Task RegisterDefinitionAsync_MetadataRejection_IdenticalAcrossProviders(
        string expectedCode, string caseName)
    {
        // Arrange
        var definition = BuildMetadataRejectionDefinition(expectedCode, caseName);
        await using var inMemoryProvider = CreateInMemoryProvider();
        await using var sqliteProvider = Database.CreateProvider();

        // Act
        var inMemoryDiagnostics = await CaptureRegistrationDiagnosticsAsync(inMemoryProvider, definition);
        var sqliteDiagnostics = await CaptureRegistrationDiagnosticsAsync(sqliteProvider, definition);

        // Assert — identical outcome, codes, and ordering across providers (SC-006).
        Assert.NotEmpty(inMemoryDiagnostics);
        Assert.Contains(expectedCode, inMemoryDiagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Equal(
            inMemoryDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)),
            sqliteDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)));
    }

    public static TheoryData<string, string> ValueRejectionCases => new()
    {
        { FacetValueDiagnosticCodes.NotAllowed, "not-allowed" },
        { FacetValueDiagnosticCodes.LengthOutOfRange, "length-out-of-range" },
        { FacetValueDiagnosticCodes.OutOfRange, "out-of-range" },
        { FacetValueDiagnosticCodes.PatternMismatch, "pattern-mismatch" },
    };

    [SkippableTheory]
    [MemberData(nameof(ValueRejectionCases))]
    public async Task CreateAsync_ValueRejection_IdenticalAcrossProviders(
        string expectedCode, string caseName)
    {
        // Arrange
        var (definition, aspects) = BuildValueRejectionCase(expectedCode, caseName);
        await using var inMemoryProvider = CreateInMemoryProvider();
        await using var sqliteProvider = Database.CreateProvider();

        // Act
        var inMemoryDiagnostics = await CaptureSaveDiagnosticsAsync(inMemoryProvider, definition, aspects);
        var sqliteDiagnostics = await CaptureSaveDiagnosticsAsync(sqliteProvider, definition, aspects);

        // Assert — identical outcome, codes, and ordering across providers (SC-006).
        Assert.NotEmpty(inMemoryDiagnostics);
        Assert.Contains(expectedCode, inMemoryDiagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Equal(
            inMemoryDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)),
            sqliteDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)));
    }

    [SkippableFact]
    public async Task CreateAsync_MultipleValueViolations_OrderingIdenticalAcrossProviders()
    {
        // Arrange — one facet violating length + pattern, plus a numeric facet out of range.
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("ParityOrdering")
            .WithTypedAspect<CountAspect>()
            .WithFacetMetadata(new FacetMetadata { Minimum = 10 })
            .WithTypedAspect<TitleAspect>()
            .WithFacetMetadata(new FacetMetadata { MinLength = 5, Pattern = "^[a-z]+$" })
            .Build();
        var aspects = new Dictionary<string, object>
        {
            ["CountAspect"] = new Dictionary<string, object> { ["Int32"] = 3 },
            ["TitleAspect"] = new Dictionary<string, object> { ["String"] = "AB" },
        };
        await using var inMemoryProvider = CreateInMemoryProvider();
        await using var sqliteProvider = Database.CreateProvider();

        // Act
        var inMemoryDiagnostics = await CaptureSaveDiagnosticsAsync(inMemoryProvider, definition, aspects);
        var sqliteDiagnostics = await CaptureSaveDiagnosticsAsync(sqliteProvider, definition, aspects);

        // Assert — deterministic aspect → facet → code ordering, identical across providers.
        Assert.Equal(
            (IEnumerable<string>)
            [
                FacetValueDiagnosticCodes.OutOfRange,
                FacetValueDiagnosticCodes.LengthOutOfRange,
                FacetValueDiagnosticCodes.PatternMismatch,
            ],
            inMemoryDiagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Equal(
            inMemoryDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)),
            sqliteDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ResourceDefinition BuildMetadataRejectionDefinition(string code, string caseName)
    {
        var metadata = code switch
        {
            FacetMetadataDiagnosticCodes.RangeInvalid => new FacetMetadata { MinLength = 10, MaxLength = 2 },
            FacetMetadataDiagnosticCodes.ConstraintInapplicable => new FacetMetadata { Minimum = 1 },
            FacetMetadataDiagnosticCodes.AllowedValuesEmpty => new FacetMetadata { AllowedValues = [] },
            FacetMetadataDiagnosticCodes.AllowedValuesDuplicate => new FacetMetadata { AllowedValues = ["a", "a"] },
            FacetMetadataDiagnosticCodes.DefaultValueInvalid => new FacetMetadata { AllowedValues = ["a", "b"], DefaultValue = "c" },
            FacetMetadataDiagnosticCodes.PatternInvalid => new FacetMetadata { Pattern = "[unclosed" },
            _ => null,
        };

        if (metadata is not null)
        {
            return new ResourceDefinitionBuilder()
                .WithDefinitionId($"Parity-{caseName}")
                .WithTypedAspect<TitleAspect>()
                .WithFacetMetadata(metadata)
                .Build();
        }

        // AllowedValueTypeMismatch requires a numeric facet.
        return new ResourceDefinitionBuilder()
            .WithDefinitionId($"Parity-{caseName}")
            .WithTypedAspect<CountAspect>()
            .WithFacetMetadata(new FacetMetadata { AllowedValues = ["not-a-number"] })
            .Build();
    }

    private static (ResourceDefinition Definition, Dictionary<string, object> Aspects) BuildValueRejectionCase(
        string code, string caseName)
    {
        if (code == FacetValueDiagnosticCodes.OutOfRange)
        {
            var numericDefinition = new ResourceDefinitionBuilder()
                .WithDefinitionId($"ValueParity-{caseName}")
                .WithTypedAspect<CountAspect>()
                .WithFacetMetadata(new FacetMetadata { Minimum = 10, Maximum = 100 })
                .Build();
            return (numericDefinition, new Dictionary<string, object>
            {
                ["CountAspect"] = new Dictionary<string, object> { ["Int32"] = 3 },
            });
        }

        var (metadata, value) = code switch
        {
            FacetValueDiagnosticCodes.NotAllowed =>
                (new FacetMetadata { AllowedValues = ["a", "b"] }, "c"),
            FacetValueDiagnosticCodes.LengthOutOfRange =>
                (new FacetMetadata { MinLength = 5 }, "ab"),
            _ =>
                (new FacetMetadata { Pattern = "^[a-z]+$" }, "ABC"),
        };

        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId($"ValueParity-{caseName}")
            .WithTypedAspect<TitleAspect>()
            .WithFacetMetadata(metadata)
            .Build();
        return (definition, new Dictionary<string, object>
        {
            ["TitleAspect"] = new Dictionary<string, object> { ["String"] = value },
        });
    }

    private static async Task<IReadOnlyList<FacetMetadataDiagnostic>> CaptureRegistrationDiagnosticsAsync(
        ServiceProvider provider, ResourceDefinition definition)
    {
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(
            () => store.RegisterDefinitionAsync(definition).AsTask());
        return exception.Diagnostics;
    }

    private static async Task<IReadOnlyList<FacetValueDiagnostic>> CaptureSaveDiagnosticsAsync(
        ServiceProvider provider, ResourceDefinition definition, Dictionary<string, object> aspects)
    {
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        await store.RegisterDefinitionAsync(definition);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(
            () => manager.CreateAsync(definition.DefinitionId, new CreateResourceRequest
            {
                InitialAspects = aspects,
            }).AsTask());
        return exception.Diagnostics;
    }

    private static string? GetString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => value?.ToString(),
    };
}

public sealed class SqliteJsonFacetMetadataTests() : FacetMetadataStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbFacetMetadataTests() : FacetMetadataStoreTests(new PostgresTestDatabase());
