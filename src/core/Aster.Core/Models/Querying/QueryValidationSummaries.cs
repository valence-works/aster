namespace Aster.Core.Models.Querying;

/// <summary>
/// Deterministic count for one validation failure code.
/// </summary>
public sealed record QueryValidationFailureCodeCount
{
    /// <summary>Stable validation failure code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of failures with the code.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one validation failure path.
/// </summary>
public sealed record QueryValidationFailurePathCount
{
    /// <summary>Query path associated with validation failures.</summary>
    public required string Path { get; init; }

    /// <summary>Number of failures with the path.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one validation failure feature category.
/// </summary>
public sealed record QueryValidationFailureFeatureCount
{
    /// <summary>Feature category associated with validation failures.</summary>
    public required string Feature { get; init; }

    /// <summary>Number of failures with the feature category.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over a query validation result.
/// </summary>
public sealed record QueryValidationSummary
{
    /// <summary>Total number of validation failures.</summary>
    public required int TotalFailureCount { get; init; }

    /// <summary>Whether the validation result has no failures.</summary>
    public bool IsValid => TotalFailureCount == 0;

    /// <summary>Whether the validation result has one or more failures.</summary>
    public bool HasFailures => TotalFailureCount > 0;

    /// <summary>Deterministic counts by nonblank validation failure code.</summary>
    public IReadOnlyList<QueryValidationFailureCodeCount> FailureCodeCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank validation failure path.</summary>
    public IReadOnlyList<QueryValidationFailurePathCount> FailurePathCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank validation failure feature category.</summary>
    public IReadOnlyList<QueryValidationFailureFeatureCount> FailureFeatureCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for query validation result objects.
/// </summary>
public static class QueryValidationSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for a query validation result.
    /// </summary>
    /// <param name="result">The query validation result to summarize.</param>
    /// <returns>A summary over validation failures by code, path, and feature.</returns>
    public static QueryValidationSummary ToSummary(this QueryValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var failures = result.Failures ?? [];

        return new QueryValidationSummary
        {
            TotalFailureCount = failures.Count,
            FailureCodeCounts = CountBy(
                failures.Select(static failure => failure.Code),
                static (key, count) => new QueryValidationFailureCodeCount
                {
                    Code = key,
                    Count = count,
                }),
            FailurePathCounts = CountBy(
                failures.Select(static failure => failure.Path),
                static (key, count) => new QueryValidationFailurePathCount
                {
                    Path = key,
                    Count = count,
                }),
            FailureFeatureCounts = CountBy(
                failures.Select(static failure => failure.Feature),
                static (key, count) => new QueryValidationFailureFeatureCount
                {
                    Feature = key,
                    Count = count,
                }),
        };
    }

    private static IReadOnlyList<TCount> CountBy<TCount>(
        IEnumerable<string?> keys,
        Func<string, int, TCount> createCount) =>
        keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .GroupBy(static key => key!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => createCount(group.Key, group.Count()))
            .ToList();
}
