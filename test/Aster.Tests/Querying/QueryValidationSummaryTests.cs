using Aster.Core.Models.Querying;

namespace Aster.Tests.Querying;

public sealed class QueryValidationSummaryTests
{
    [Fact]
    public void Summary_SuccessResult_ReturnsValidZeroCounts()
    {
        var summary = QueryValidationResult.Success.ToSummary();

        Assert.True(summary.IsValid);
        Assert.False(summary.HasFailures);
        Assert.Equal(0, summary.TotalFailureCount);
        Assert.Empty(summary.FailureCodeCounts);
        Assert.Empty(summary.FailurePathCounts);
        Assert.Empty(summary.FailureFeatureCounts);
    }

    [Fact]
    public void Summary_MixedFailures_AggregatesCodeCountsDeterministically()
    {
        var summary = new QueryValidationResult(
            [
                Failure("z-code"),
                Failure("a-code"),
                Failure("z-code"),
                Failure(""),
                Failure(" "),
            ]).ToSummary();

        Assert.False(summary.IsValid);
        Assert.True(summary.HasFailures);
        Assert.Equal(5, summary.TotalFailureCount);
        Assert.Equal(
            [("a-code", 1), ("z-code", 2)],
            summary.FailureCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void Summary_MixedFailures_AggregatesPathCountsDeterministically()
    {
        var summary = new QueryValidationResult(
            [
                Failure("unsupported-scope", path: "Scope"),
                Failure("unsupported-operator", path: "Filter.Operator"),
                Failure("unsupported-operator", path: "Filter.Operator"),
                Failure("unsupported-paging", path: ""),
                Failure("unsupported-paging", path: " "),
                Failure("unsupported-paging", path: null),
            ]).ToSummary();

        Assert.Equal(
            [("Filter.Operator", 2), ("Scope", 1)],
            summary.FailurePathCounts.Select(static count => (count.Path, count.Count)).ToList());
    }

    [Fact]
    public void Summary_MixedFailures_AggregatesFeatureCountsDeterministically()
    {
        var summary = new QueryValidationResult(
            [
                Failure("unsupported-scope", feature: "scope"),
                Failure("unsupported-operator", feature: "operator"),
                Failure("unsupported-operator", feature: "operator"),
                Failure("unsupported-paging", feature: ""),
                Failure("unsupported-paging", feature: " "),
                Failure("unsupported-paging", feature: null),
            ]).ToSummary();

        Assert.Equal(
            [("operator", 2), ("scope", 1)],
            summary.FailureFeatureCounts.Select(static count => (count.Feature, count.Count)).ToList());
    }

    [Fact]
    public void Summary_NullFailureCollection_IsTreatedAsEmpty()
    {
        var summary = new QueryValidationResult(null!).ToSummary();

        Assert.True(summary.IsValid);
        Assert.Equal(0, summary.TotalFailureCount);
        Assert.Empty(summary.FailureCodeCounts);
    }

    [Fact]
    public void Summary_NullResultThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ((QueryValidationResult)null!).ToSummary());
    }

    private static QueryValidationFailure Failure(
        string code,
        string? path = null,
        string? feature = null) =>
        new(code, code, path, feature);
}
