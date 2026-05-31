namespace Aster.Core.Models.Lifecycle;

/// <summary>
/// Deterministic count for one lifecycle hook outcome status.
/// </summary>
public sealed record LifecycleHookOutcomeStatusCount
{
    /// <summary>Lifecycle hook outcome status.</summary>
    public required LifecycleHookOutcomeStatus Status { get; init; }

    /// <summary>Number of outcomes with the status.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one lifecycle hook outcome code.
/// </summary>
public sealed record LifecycleHookOutcomeCodeCount
{
    /// <summary>Stable outcome code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of outcomes with the code.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one lifecycle hook diagnostic code.
/// </summary>
public sealed record LifecycleHookDiagnosticCodeCount
{
    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of diagnostics with the code.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one lifecycle hook diagnostic lifecycle point.
/// </summary>
public sealed record LifecycleHookDiagnosticLifecyclePointCount
{
    /// <summary>Diagnostic lifecycle point.</summary>
    public required LifecyclePoint LifecyclePoint { get; init; }

    /// <summary>Number of diagnostics with the lifecycle point.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one lifecycle hook diagnostic hook type.
/// </summary>
public sealed record LifecycleHookDiagnosticHookTypeCount
{
    /// <summary>Hook type name.</summary>
    public required string HookType { get; init; }

    /// <summary>Number of diagnostics with the hook type.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over lifecycle hook outcomes.
/// </summary>
public sealed record LifecycleHookOutcomeSummary
{
    /// <summary>Total number of supplied outcomes.</summary>
    public required int TotalOutcomeCount { get; init; }

    /// <summary>Number of continue outcomes.</summary>
    public required int ContinueCount { get; init; }

    /// <summary>Number of rejected outcomes.</summary>
    public required int RejectedCount { get; init; }

    /// <summary>Number of failed outcomes.</summary>
    public required int FailedCount { get; init; }

    /// <summary>Total number of nested diagnostics.</summary>
    public required int TotalDiagnosticCount { get; init; }

    /// <summary>Whether every supplied outcome can continue.</summary>
    public bool IsFullySuccessful => TotalOutcomeCount == ContinueCount;

    /// <summary>Whether one or more outcomes were rejected.</summary>
    public bool HasRejectedOutcomes => RejectedCount > 0;

    /// <summary>Whether one or more outcomes failed.</summary>
    public bool HasFailedOutcomes => FailedCount > 0;

    /// <summary>Whether one or more nested diagnostics are present.</summary>
    public bool HasDiagnostics => TotalDiagnosticCount > 0;

    /// <summary>Deterministic counts by outcome status.</summary>
    public IReadOnlyList<LifecycleHookOutcomeStatusCount> StatusCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank outcome code.</summary>
    public IReadOnlyList<LifecycleHookOutcomeCodeCount> OutcomeCodeCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank diagnostic code.</summary>
    public IReadOnlyList<LifecycleHookDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];

    /// <summary>Deterministic counts by diagnostic lifecycle point.</summary>
    public IReadOnlyList<LifecycleHookDiagnosticLifecyclePointCount> DiagnosticLifecyclePointCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank diagnostic hook type.</summary>
    public IReadOnlyList<LifecycleHookDiagnosticHookTypeCount> DiagnosticHookTypeCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for lifecycle hook outcome objects.
/// </summary>
public static class LifecycleHookOutcomeSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for one lifecycle hook outcome.
    /// </summary>
    /// <param name="outcome">The lifecycle hook outcome to summarize.</param>
    /// <returns>A summary over the supplied outcome.</returns>
    public static LifecycleHookOutcomeSummary ToSummary(this LifecycleHookOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new[] { outcome }.ToSummary();
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for lifecycle hook outcomes.
    /// </summary>
    /// <param name="outcomes">The lifecycle hook outcomes to summarize.</param>
    /// <returns>A summary over the supplied outcomes.</returns>
    public static LifecycleHookOutcomeSummary ToSummary(this IEnumerable<LifecycleHookOutcome>? outcomes)
    {
        var materializedOutcomes = (outcomes ?? [])
            .Where(static outcome => outcome is not null)
            .ToList();
        var diagnostics = materializedOutcomes
            .SelectMany(static outcome => outcome.Diagnostics ?? [])
            .ToList();

        return new LifecycleHookOutcomeSummary
        {
            TotalOutcomeCount = materializedOutcomes.Count,
            ContinueCount = materializedOutcomes.Count(static outcome => outcome.Status == LifecycleHookOutcomeStatus.Continue),
            RejectedCount = materializedOutcomes.Count(static outcome => outcome.Status == LifecycleHookOutcomeStatus.Rejected),
            FailedCount = materializedOutcomes.Count(static outcome => outcome.Status == LifecycleHookOutcomeStatus.Failed),
            TotalDiagnosticCount = diagnostics.Count,
            StatusCounts = materializedOutcomes
                .GroupBy(static outcome => outcome.Status)
                .OrderBy(static group => group.Key)
                .Select(static group => new LifecycleHookOutcomeStatusCount
                {
                    Status = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            OutcomeCodeCounts = CountBy(
                materializedOutcomes.Select(static outcome => outcome.Code),
                static (key, count) => new LifecycleHookOutcomeCodeCount
                {
                    Code = key,
                    Count = count,
                }),
            DiagnosticCodeCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.Code),
                static (key, count) => new LifecycleHookDiagnosticCodeCount
                {
                    Code = key,
                    Count = count,
                }),
            DiagnosticLifecyclePointCounts = diagnostics
                .GroupBy(static diagnostic => diagnostic.LifecyclePoint)
                .OrderBy(static group => group.Key)
                .Select(static group => new LifecycleHookDiagnosticLifecyclePointCount
                {
                    LifecyclePoint = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            DiagnosticHookTypeCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.HookType),
                static (key, count) => new LifecycleHookDiagnosticHookTypeCount
                {
                    HookType = key,
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
