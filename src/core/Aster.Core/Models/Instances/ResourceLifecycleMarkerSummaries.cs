using Aster.Core.Models.Policies;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Deterministic count for one lifecycle marker state.
/// </summary>
public sealed record ResourceLifecycleMarkerStateCount
{
    /// <summary>Lifecycle marker state.</summary>
    public required ResourceLifecycleMarkerState State { get; init; }

    /// <summary>Number of markers with the state.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one lifecycle marker resource identifier.
/// </summary>
public sealed record ResourceLifecycleMarkerResourceCount
{
    /// <summary>Resource identifier.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Number of markers with the resource identifier.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over one or more lifecycle marker write results.
/// </summary>
public sealed record ResourceLifecycleMarkerResultSummary
{
    /// <summary>Total number of supplied non-null marker results.</summary>
    public required int TotalResultCount { get; init; }

    /// <summary>Number of marker results that succeeded.</summary>
    public required int SucceededCount { get; init; }

    /// <summary>Number of marker results that failed.</summary>
    public required int FailedCount { get; init; }

    /// <summary>Number of marker results that include an effective marker.</summary>
    public required int MarkerPresentCount { get; init; }

    /// <summary>Number of marker results without an effective marker.</summary>
    public required int MissingMarkerCount { get; init; }

    /// <summary>Total number of marker result diagnostics.</summary>
    public required int TotalDiagnosticCount { get; init; }

    /// <summary>Number of distinct nonblank marker resource identifiers.</summary>
    public required int DistinctMarkerResourceCount { get; init; }

    /// <summary>Whether every supplied result succeeded.</summary>
    public bool IsFullySuccessful => FailedCount == 0;

    /// <summary>Whether any supplied result failed.</summary>
    public bool HasFailures => FailedCount > 0;

    /// <summary>Whether any supplied result has diagnostics.</summary>
    public bool HasDiagnostics => TotalDiagnosticCount > 0;

    /// <summary>Deterministic marker state counts.</summary>
    public IReadOnlyList<ResourceLifecycleMarkerStateCount> MarkerStateCounts { get; init; } = [];

    /// <summary>Deterministic marker resource identifier counts.</summary>
    public IReadOnlyList<ResourceLifecycleMarkerResourceCount> MarkerResourceCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic code counts.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic path counts.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticPathCount> DiagnosticPathCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic resource identifier counts.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticResourceIdCount> DiagnosticResourceIdCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for lifecycle marker write result objects.
/// </summary>
public static class ResourceLifecycleMarkerResultSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for one lifecycle marker write result.
    /// </summary>
    /// <param name="result">The marker result to summarize.</param>
    /// <returns>A summary over marker success, marker state, marker resource, and diagnostics.</returns>
    public static ResourceLifecycleMarkerResultSummary ToSummary(this ResourceLifecycleMarkerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new[] { result }.ToSummary();
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for lifecycle marker write results.
    /// </summary>
    /// <param name="results">The marker results to summarize.</param>
    /// <returns>A summary over marker success, marker state, marker resource, and diagnostics.</returns>
    public static ResourceLifecycleMarkerResultSummary ToSummary(this IEnumerable<ResourceLifecycleMarkerResult>? results)
    {
        var materialized = (results ?? []).Where(static result => result is not null).ToList();
        var markers = materialized.Select(static result => result.Marker).Where(static marker => marker is not null).ToList();
        var diagnostics = materialized.SelectMany(static result => result.Diagnostics ?? []).ToList();
        var succeededCount = materialized.Count(static result => (result.Diagnostics ?? []).Count == 0);

        var markerResourceCounts = CountBy(
            markers.Select(static marker => marker!.ResourceId),
            static (key, count) => new ResourceLifecycleMarkerResourceCount
            {
                ResourceId = key,
                Count = count,
            });

        return new ResourceLifecycleMarkerResultSummary
        {
            TotalResultCount = materialized.Count,
            SucceededCount = succeededCount,
            FailedCount = materialized.Count - succeededCount,
            MarkerPresentCount = markers.Count,
            MissingMarkerCount = materialized.Count - markers.Count,
            TotalDiagnosticCount = diagnostics.Count,
            DistinctMarkerResourceCount = markerResourceCounts.Count,
            MarkerStateCounts = markers
                .GroupBy(static marker => marker!.State)
                .OrderBy(static group => group.Key)
                .Select(static group => new ResourceLifecycleMarkerStateCount
                {
                    State = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            MarkerResourceCounts = markerResourceCounts,
            DiagnosticCodeCounts = ResourcePolicyDiagnosticCodeCounter.Count(diagnostics),
            DiagnosticPathCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.Path),
                static (key, count) => new ResourcePolicyDiagnosticPathCount
                {
                    Path = key,
                    Count = count,
                }),
            DiagnosticResourceIdCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.ResourceId),
                static (key, count) => new ResourcePolicyDiagnosticResourceIdCount
                {
                    ResourceId = key,
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
