using Aster.Core.Models.Querying;

namespace Aster.Tests.Querying;

public sealed class IndexProjectionSummaryTests
{
    [Fact]
    public void ValidationSummary_SuccessResult_ReturnsValidZeroCounts()
    {
        var summary = IndexProjectionValidationResult.Success.ToSummary();

        Assert.True(summary.IsValid);
        Assert.False(summary.HasFailures);
        Assert.Equal(0, summary.TotalFailureCount);
        Assert.Empty(summary.FailureCodeCounts);
        Assert.Empty(summary.FailureFieldCounts);
        Assert.Empty(summary.FailureSourceCounts);
    }

    [Fact]
    public void ValidationSummary_MixedFailures_AggregatesCountsDeterministically()
    {
        var summary = new IndexProjectionValidationResult(
            [
                Failure("title", IndexProjectionFailureCodes.InvalidProjectionDeclaration, "facet 'Title.Title'"),
                Failure("title", IndexProjectionFailureCodes.InvalidProjectionDeclaration, "facet 'Title.Title'"),
                Failure("duplicate", IndexProjectionFailureCodes.DuplicateProjectionField, "metadata 'ResourceId'"),
                Failure("z-field", IndexProjectionFailureCodes.MissingSource, "facet 'Z.Value'"),
            ]).ToSummary();

        Assert.False(summary.IsValid);
        Assert.True(summary.HasFailures);
        Assert.Equal(4, summary.TotalFailureCount);
        Assert.Equal(
            [
                (IndexProjectionFailureCodes.DuplicateProjectionField, 1),
                (IndexProjectionFailureCodes.InvalidProjectionDeclaration, 2),
                (IndexProjectionFailureCodes.MissingSource, 1),
            ],
            summary.FailureCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [("duplicate", 1), ("title", 2), ("z-field", 1)],
            summary.FailureFieldCounts.Select(static count => (count.FieldName, count.Count)).ToList());
        Assert.Equal(
            [("facet 'Title.Title'", 2), ("facet 'Z.Value'", 1), ("metadata 'ResourceId'", 1)],
            summary.FailureSourceCounts.Select(static count => (count.Source, count.Count)).ToList());
    }

    [Fact]
    public void ValidationSummary_BlankFailureKeys_AreIgnoredInKeyCounts()
    {
        var summary = new IndexProjectionValidationResult(
            [
                Failure("", "", ""),
                Failure(" ", " ", " "),
                Failure(null, IndexProjectionFailureCodes.InvalidProjectionDeclaration, null),
            ]).ToSummary();

        Assert.Equal(3, summary.TotalFailureCount);
        Assert.Equal(
            [(IndexProjectionFailureCodes.InvalidProjectionDeclaration, 1)],
            summary.FailureCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Empty(summary.FailureFieldCounts);
        Assert.Empty(summary.FailureSourceCounts);
    }

    [Fact]
    public void EvaluationSummary_ValuesOnly_AggregatesFieldTypeAndFieldCounts()
    {
        var summary = new IndexProjectionEvaluationResult(
            [
                Value("title", IndexFieldType.Keyword, "Alpha"),
                Value("tags", IndexFieldType.KeywordArray, new[] { "featured" }),
                Value("created", IndexFieldType.DateTime, "2026-01-01T00:00:00.0000000Z"),
                Value("title_copy", IndexFieldType.Keyword, "Alpha"),
            ],
            []).ToSummary();

        Assert.True(summary.IsValid);
        Assert.False(summary.HasFailures);
        Assert.True(summary.HasValues);
        Assert.Equal(4, summary.TotalValueCount);
        Assert.Equal(0, summary.TotalFailureCount);
        Assert.Equal(
            [(IndexFieldType.Keyword, 2), (IndexFieldType.DateTime, 1), (IndexFieldType.KeywordArray, 1)],
            summary.ValueFieldTypeCounts.Select(static count => (count.FieldType, count.Count)).ToList());
        Assert.Equal(
            [("created", 1), ("tags", 1), ("title", 1), ("title_copy", 1)],
            summary.ValueFieldCounts.Select(static count => (count.FieldName, count.Count)).ToList());
    }

    [Fact]
    public void EvaluationSummary_MixedValuesAndFailures_PreservesBothSides()
    {
        var summary = new IndexProjectionEvaluationResult(
            [
                Value("title", IndexFieldType.Keyword, "Alpha"),
                Value("created", IndexFieldType.DateTime, "2026-01-01T00:00:00.0000000Z"),
            ],
            [
                Failure("missing", IndexProjectionFailureCodes.MissingSource, "facet 'Missing.Value'"),
                Failure("bad_integer", IndexProjectionFailureCodes.IncompatibleValueShape, "facet 'Metrics.Count'"),
                Failure("bad_decimal", IndexProjectionFailureCodes.IncompatibleValueShape, "facet 'Metrics.Amount'"),
            ]).ToSummary();

        Assert.False(summary.IsValid);
        Assert.True(summary.HasFailures);
        Assert.True(summary.HasValues);
        Assert.Equal(2, summary.TotalValueCount);
        Assert.Equal(3, summary.TotalFailureCount);
        Assert.Equal(
            [(IndexProjectionFailureCodes.IncompatibleValueShape, 2), (IndexProjectionFailureCodes.MissingSource, 1)],
            summary.FailureCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
        Assert.Equal(
            [("bad_decimal", 1), ("bad_integer", 1), ("missing", 1)],
            summary.FailureFieldCounts.Select(static count => (count.FieldName, count.Count)).ToList());
        Assert.Equal(
            [("facet 'Metrics.Amount'", 1), ("facet 'Metrics.Count'", 1), ("facet 'Missing.Value'", 1)],
            summary.FailureSourceCounts.Select(static count => (count.Source, count.Count)).ToList());
    }

    [Fact]
    public void EvaluationSummary_NullNestedCollections_AreTreatedAsEmpty()
    {
        var summary = new IndexProjectionEvaluationResult(null!, null!).ToSummary();

        Assert.True(summary.IsValid);
        Assert.False(summary.HasValues);
        Assert.Equal(0, summary.TotalValueCount);
        Assert.Equal(0, summary.TotalFailureCount);
        Assert.Empty(summary.ValueFieldTypeCounts);
        Assert.Empty(summary.ValueFieldCounts);
        Assert.Empty(summary.FailureCodeCounts);
    }

    [Fact]
    public void Summaries_NullInputsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ((IndexProjectionValidationResult)null!).ToSummary());
        Assert.Throws<ArgumentNullException>(() => ((IndexProjectionEvaluationResult)null!).ToSummary());
    }

    private static IndexProjectionFailure Failure(string? fieldName, string code, string? source) =>
        new(fieldName, code, code, source);

    private static IndexProjectionValue Value(string fieldName, IndexFieldType fieldType, object value) =>
        new(fieldName, fieldType, value);
}
