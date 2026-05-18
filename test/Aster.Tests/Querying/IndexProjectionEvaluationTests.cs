using System.Text.Json;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;

namespace Aster.Tests.Querying;

public sealed class IndexProjectionEvaluationTests
{
    private readonly IndexProjectionEvaluator evaluator = new();

    [Fact]
    public void Evaluate_ReturnsTypedValuesForMetadataAndFacetProjections()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Title"] = new { Title = "Alpha Gadget", Published = true },
            ["Metrics"] = new Dictionary<string, object>
            {
                ["Count"] = 3,
                ["Amount"] = 12.5m,
            },
            ["External"] = new { Id = Guid.Parse("9c5f7b3e-cc0f-44b1-80c5-cc9aa818f0cf") },
            ["Taxonomy"] = new { Tags = new[] { "featured", "sale" } },
        });
        var projections = new[]
        {
            IndexProjection.Metadata("resource_id", "ResourceId", IndexFieldType.Keyword),
            IndexProjection.Metadata("version", "Version", IndexFieldType.Integer),
            IndexProjection.Metadata("created", "Created", IndexFieldType.DateTime),
            IndexProjection.Facet("title", "Title", "Title", IndexFieldType.NormalizedText),
            IndexProjection.Facet("published", "Title", "Published", IndexFieldType.Boolean),
            IndexProjection.Facet("count", "Metrics", "Count", IndexFieldType.Integer),
            IndexProjection.Facet("amount", "Metrics", "Amount", IndexFieldType.Decimal),
            IndexProjection.Facet("external_id", "External", "Id", IndexFieldType.Guid),
            IndexProjection.Facet("tags", "Taxonomy", "Tags", IndexFieldType.KeywordArray),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
        Assert.Equal("product-a", Value<string>(result, "resource_id"));
        Assert.Equal(2L, Value<long>(result, "version"));
        Assert.Equal("2026-02-01T10:30:00.0000000Z", Value<string>(result, "created"));
        Assert.Equal("Alpha Gadget", Value<string>(result, "title"));
        Assert.True(Value<bool>(result, "published"));
        Assert.Equal(3L, Value<long>(result, "count"));
        Assert.Equal(12.5m, Value<decimal>(result, "amount"));
        Assert.Equal(Guid.Parse("9c5f7b3e-cc0f-44b1-80c5-cc9aa818f0cf"), Value<Guid>(result, "external_id"));
        Assert.Collection(
            Value<string[]>(result, "tags"),
            value => Assert.Equal("featured", value),
            value => Assert.Equal("sale", value));
    }

    [Fact]
    public void Evaluate_ReturnsFailuresWithoutDiscardingSuccessfulValues()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Title"] = new { Title = "Alpha Gadget", Aliases = "not-an-array" },
            ["Metrics"] = new { Count = "3", History = new[] { 1, 2 } },
        });
        var projections = new[]
        {
            IndexProjection.Facet("title", "Title", "Title", IndexFieldType.Keyword),
            IndexProjection.Facet("missing_aspect", "Missing", "Value", IndexFieldType.Keyword),
            IndexProjection.Facet("missing_facet", "Title", "Missing", IndexFieldType.Keyword),
            IndexProjection.Facet("bad_integer", "Metrics", "Count", IndexFieldType.Integer),
            IndexProjection.Facet("array_as_scalar", "Metrics", "History", IndexFieldType.Integer),
            IndexProjection.Facet("scalar_as_array", "Title", "Aliases", IndexFieldType.KeywordArray),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.False(result.IsValid);
        Assert.Equal("Alpha Gadget", Value<string>(result, "title"));
        Assert.Equal(5, result.Failures.Count);
        Assert.Contains(result.Failures, Failure("missing_aspect", IndexProjectionFailureCodes.MissingSource));
        Assert.Contains(result.Failures, Failure("missing_facet", IndexProjectionFailureCodes.MissingSource));
        Assert.Contains(result.Failures, Failure("bad_integer", IndexProjectionFailureCodes.IncompatibleValueShape));
        Assert.Contains(result.Failures, Failure("array_as_scalar", IndexProjectionFailureCodes.IncompatibleValueShape));
        Assert.Contains(result.Failures, Failure("scalar_as_array", IndexProjectionFailureCodes.IncompatibleValueShape));
    }

    [Fact]
    public void Evaluate_DoesNotCoerceNumericBooleanOrGuidLookingStrings()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Facet"] = new
            {
                Count = "3",
                Published = "true",
                Id = "9c5f7b3e-cc0f-44b1-80c5-cc9aa818f0cf",
            },
        });
        var projections = new[]
        {
            IndexProjection.Facet("count", "Facet", "Count", IndexFieldType.Integer),
            IndexProjection.Facet("published", "Facet", "Published", IndexFieldType.Boolean),
            IndexProjection.Facet("id", "Facet", "Id", IndexFieldType.Guid),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.Empty(result.Values);
        Assert.Equal(3, result.Failures.Count);
        Assert.All(result.Failures, failure => Assert.Equal(IndexProjectionFailureCodes.IncompatibleValueShape, failure.Code));
    }

    [Fact]
    public void Evaluate_NormalizesAcceptedDateTimeValuesAndRejectsDateOnlyStrings()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Schedule"] = new
            {
                StartsAt = new DateTimeOffset(2026, 3, 1, 12, 15, 0, TimeSpan.FromHours(2)),
                EndsAt = "2026-03-01T15:30:00+01:00",
                DateOnly = "2026-03-01",
            },
        });
        var projections = new[]
        {
            IndexProjection.Facet("starts_at", "Schedule", "StartsAt", IndexFieldType.DateTime),
            IndexProjection.Facet("ends_at", "Schedule", "EndsAt", IndexFieldType.DateTime),
            IndexProjection.Facet("date_only", "Schedule", "DateOnly", IndexFieldType.DateTime),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.Equal("2026-03-01T10:15:00.0000000Z", Value<string>(result, "starts_at"));
        Assert.Equal("2026-03-01T14:30:00.0000000Z", Value<string>(result, "ends_at"));
        var failure = Assert.Single(result.Failures);
        Assert.Equal("date_only", failure.FieldName);
        Assert.Equal(IndexProjectionFailureCodes.IncompatibleValueShape, failure.Code);
    }

    [Fact]
    public void Evaluate_ResolvesJsonElementFacetValues()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "title": "Alpha Gadget",
              "tags": ["featured", "sale"]
            }
            """);
        var resource = CreateResource(aspects: new()
        {
            ["JsonFacet"] = document.RootElement.Clone(),
        });
        var projections = new[]
        {
            IndexProjection.Facet("title", "JsonFacet", "Title", IndexFieldType.Keyword),
            IndexProjection.Facet("tags", "JsonFacet", "Tags", IndexFieldType.KeywordArray),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.True(result.IsValid);
        Assert.Equal("Alpha Gadget", Value<string>(result, "title"));
        Assert.Collection(
            Value<string[]>(result, "tags"),
            value => Assert.Equal("featured", value),
            value => Assert.Equal("sale", value));
    }

    [Fact]
    public void Evaluate_ReportsDuplicateProjectionFieldWithoutDiscardingFirstValue()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Title"] = new { Title = "Alpha Gadget", OtherTitle = "Beta Gadget" },
        });
        var projections = new[]
        {
            IndexProjection.Facet("title", "Title", "Title", IndexFieldType.Keyword),
            IndexProjection.Facet("title", "Title", "OtherTitle", IndexFieldType.Keyword),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.False(result.IsValid);
        Assert.Equal("Alpha Gadget", Value<string>(result, "title"));
        var failure = Assert.Single(result.Failures);
        Assert.Equal("title", failure.FieldName);
        Assert.Equal(IndexProjectionFailureCodes.DuplicateProjectionField, failure.Code);
    }

    [Fact]
    public void Evaluate_DoesNotReturnValuesForInvalidEmptyFieldNames()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Title"] = new { Title = "Alpha Gadget" },
        });
        var projections = new[]
        {
            IndexProjection.Facet("", "Title", "Title", IndexFieldType.Keyword),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.Empty(result.Values);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("", failure.FieldName);
        Assert.Equal(IndexProjectionFailureCodes.InvalidProjectionDeclaration, failure.Code);
    }

    [Fact]
    public void Evaluate_NormalizesAllSupportedClrIntegerValues()
    {
        var resource = CreateResource(aspects: new()
        {
            ["Numbers"] = new Dictionary<string, object>
            {
                ["Byte"] = (byte)1,
                ["SByte"] = (sbyte)-2,
                ["Short"] = (short)-3,
                ["UShort"] = (ushort)4,
                ["Int"] = -5,
                ["UInt"] = 6u,
                ["Long"] = -7L,
                ["ULong"] = 8ul,
                ["TooLargeULong"] = ulong.MaxValue,
            },
        });
        var projections = new[]
        {
            IndexProjection.Facet("byte", "Numbers", "Byte", IndexFieldType.Integer),
            IndexProjection.Facet("sbyte", "Numbers", "SByte", IndexFieldType.Integer),
            IndexProjection.Facet("short", "Numbers", "Short", IndexFieldType.Integer),
            IndexProjection.Facet("ushort", "Numbers", "UShort", IndexFieldType.Integer),
            IndexProjection.Facet("int", "Numbers", "Int", IndexFieldType.Integer),
            IndexProjection.Facet("uint", "Numbers", "UInt", IndexFieldType.Integer),
            IndexProjection.Facet("long", "Numbers", "Long", IndexFieldType.Integer),
            IndexProjection.Facet("ulong", "Numbers", "ULong", IndexFieldType.Integer),
            IndexProjection.Facet("too_large_ulong", "Numbers", "TooLargeULong", IndexFieldType.Integer),
        };

        var result = evaluator.Evaluate(resource, projections);

        Assert.Equal(1L, Value<long>(result, "byte"));
        Assert.Equal(-2L, Value<long>(result, "sbyte"));
        Assert.Equal(-3L, Value<long>(result, "short"));
        Assert.Equal(4L, Value<long>(result, "ushort"));
        Assert.Equal(-5L, Value<long>(result, "int"));
        Assert.Equal(6L, Value<long>(result, "uint"));
        Assert.Equal(-7L, Value<long>(result, "long"));
        Assert.Equal(8L, Value<long>(result, "ulong"));
        var failure = Assert.Single(result.Failures);
        Assert.Equal("too_large_ulong", failure.FieldName);
        Assert.Equal(IndexProjectionFailureCodes.IncompatibleValueShape, failure.Code);
    }

    private static Resource CreateResource(Dictionary<string, object>? aspects = null) =>
        new()
        {
            ResourceId = "product-a",
            Id = "product-a-v2",
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = 2,
            Created = new DateTime(2026, 2, 1, 10, 30, 0, DateTimeKind.Utc),
            Owner = "alice",
            Hash = "abc123",
            Aspects = aspects ?? [],
        };

    private static T Value<T>(IndexProjectionEvaluationResult result, string fieldName) =>
        Assert.IsType<T>(result.Values.Single(value => value.FieldName == fieldName).Value);

    private static Predicate<IndexProjectionFailure> Failure(string fieldName, string code) =>
        failure => failure.FieldName == fieldName && failure.Code == code;
}
