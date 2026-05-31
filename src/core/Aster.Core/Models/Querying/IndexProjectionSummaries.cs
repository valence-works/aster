namespace Aster.Core.Models.Querying;

/// <summary>
/// Deterministic count for one projection failure code.
/// </summary>
public sealed record IndexProjectionFailureCodeCount
{
    /// <summary>Stable projection failure code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of failures with the code.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one projection failure field name.
/// </summary>
public sealed record IndexProjectionFailureFieldCount
{
    /// <summary>Projection field name associated with failures.</summary>
    public required string FieldName { get; init; }

    /// <summary>Number of failures with the field name.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one projection failure source description.
/// </summary>
public sealed record IndexProjectionFailureSourceCount
{
    /// <summary>Projection source description associated with failures.</summary>
    public required string Source { get; init; }

    /// <summary>Number of failures with the source description.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one successful projection value field type.
/// </summary>
public sealed record IndexProjectionValueFieldTypeCount
{
    /// <summary>Projection field type.</summary>
    public required IndexFieldType FieldType { get; init; }

    /// <summary>Number of values with the field type.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one successful projection value field name.
/// </summary>
public sealed record IndexProjectionValueFieldCount
{
    /// <summary>Projection value field name.</summary>
    public required string FieldName { get; init; }

    /// <summary>Number of values with the field name.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over an index projection validation result.
/// </summary>
public sealed record IndexProjectionValidationSummary
{
    /// <summary>Total number of projection validation failures.</summary>
    public required int TotalFailureCount { get; init; }

    /// <summary>Whether the validation result has no failures.</summary>
    public bool IsValid => TotalFailureCount == 0;

    /// <summary>Whether the validation result has one or more failures.</summary>
    public bool HasFailures => TotalFailureCount > 0;

    /// <summary>Deterministic counts by nonblank projection failure code.</summary>
    public IReadOnlyList<IndexProjectionFailureCodeCount> FailureCodeCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank projection failure field name.</summary>
    public IReadOnlyList<IndexProjectionFailureFieldCount> FailureFieldCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank projection failure source description.</summary>
    public IReadOnlyList<IndexProjectionFailureSourceCount> FailureSourceCounts { get; init; } = [];
}

/// <summary>
/// Aggregate view over an index projection evaluation result.
/// </summary>
public sealed record IndexProjectionEvaluationSummary
{
    /// <summary>Total number of successful projection values.</summary>
    public required int TotalValueCount { get; init; }

    /// <summary>Total number of projection evaluation failures.</summary>
    public required int TotalFailureCount { get; init; }

    /// <summary>Whether the evaluation result has no failures.</summary>
    public bool IsValid => TotalFailureCount == 0;

    /// <summary>Whether the evaluation result has one or more failures.</summary>
    public bool HasFailures => TotalFailureCount > 0;

    /// <summary>Whether the evaluation result has one or more successful values.</summary>
    public bool HasValues => TotalValueCount > 0;

    /// <summary>Deterministic counts by successful projection value field type.</summary>
    public IReadOnlyList<IndexProjectionValueFieldTypeCount> ValueFieldTypeCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank successful projection value field name.</summary>
    public IReadOnlyList<IndexProjectionValueFieldCount> ValueFieldCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank projection failure code.</summary>
    public IReadOnlyList<IndexProjectionFailureCodeCount> FailureCodeCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank projection failure field name.</summary>
    public IReadOnlyList<IndexProjectionFailureFieldCount> FailureFieldCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank projection failure source description.</summary>
    public IReadOnlyList<IndexProjectionFailureSourceCount> FailureSourceCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for index projection result objects.
/// </summary>
public static class IndexProjectionSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for projection validation results.
    /// </summary>
    /// <param name="result">The projection validation result to summarize.</param>
    /// <returns>A summary over projection validation failures.</returns>
    public static IndexProjectionValidationSummary ToSummary(this IndexProjectionValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var failures = result.Failures ?? [];

        return new IndexProjectionValidationSummary
        {
            TotalFailureCount = failures.Count,
            FailureCodeCounts = CountFailureCodes(failures),
            FailureFieldCounts = CountFailureFields(failures),
            FailureSourceCounts = CountFailureSources(failures),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for projection evaluation results.
    /// </summary>
    /// <param name="result">The projection evaluation result to summarize.</param>
    /// <returns>A summary over successful projection values and projection failures.</returns>
    public static IndexProjectionEvaluationSummary ToSummary(this IndexProjectionEvaluationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var values = result.Values ?? [];
        var failures = result.Failures ?? [];

        return new IndexProjectionEvaluationSummary
        {
            TotalValueCount = values.Count,
            TotalFailureCount = failures.Count,
            ValueFieldTypeCounts = values
                .GroupBy(static value => value.FieldType)
                .OrderBy(static group => group.Key)
                .Select(static group => new IndexProjectionValueFieldTypeCount
                {
                    FieldType = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            ValueFieldCounts = CountBy(
                values.Select(static value => value.FieldName),
                static (key, count) => new IndexProjectionValueFieldCount
                {
                    FieldName = key,
                    Count = count,
                }),
            FailureCodeCounts = CountFailureCodes(failures),
            FailureFieldCounts = CountFailureFields(failures),
            FailureSourceCounts = CountFailureSources(failures),
        };
    }

    private static IReadOnlyList<IndexProjectionFailureCodeCount> CountFailureCodes(
        IEnumerable<IndexProjectionFailure> failures) =>
        CountBy(
            failures.Select(static failure => failure.Code),
            static (key, count) => new IndexProjectionFailureCodeCount
            {
                Code = key,
                Count = count,
            });

    private static IReadOnlyList<IndexProjectionFailureFieldCount> CountFailureFields(
        IEnumerable<IndexProjectionFailure> failures) =>
        CountBy(
            failures.Select(static failure => failure.FieldName),
            static (key, count) => new IndexProjectionFailureFieldCount
            {
                FieldName = key,
                Count = count,
            });

    private static IReadOnlyList<IndexProjectionFailureSourceCount> CountFailureSources(
        IEnumerable<IndexProjectionFailure> failures) =>
        CountBy(
            failures.Select(static failure => failure.Source),
            static (key, count) => new IndexProjectionFailureSourceCount
            {
                Source = key,
                Count = count,
            });

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
