using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Deterministic count for one resource lifecycle marker state in version history summaries.
/// </summary>
public sealed record ResourceVersionLifecycleStateCount
{
    /// <summary>Lifecycle marker state.</summary>
    public required ResourceLifecycleMarkerState State { get; init; }

    /// <summary>Number of version summaries with the state.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over one resource version history result.
/// </summary>
public sealed record ResourceVersionHistorySummary
{
    /// <summary>Effective tenant used by the source history.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Logical resource identifier from the source history.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Total number of version summaries.</summary>
    public required int TotalVersionCount { get; init; }

    /// <summary>Number of summaries marked as latest.</summary>
    public required int LatestVersionCount { get; init; }

    /// <summary>Number of summaries marked as draft.</summary>
    public required int DraftVersionCount { get; init; }

    /// <summary>Number of summaries active in at least one channel.</summary>
    public required int ActiveVersionCount { get; init; }

    /// <summary>Number of summaries protected from destructive pruning.</summary>
    public required int ProtectedVersionCount { get; init; }

    /// <summary>Number of summaries with possible-candidate maintenance disposition.</summary>
    public required int PossibleCandidateCount { get; init; }

    /// <summary>Deterministic lifecycle marker state counts across version summaries.</summary>
    public IReadOnlyList<ResourceVersionLifecycleStateCount> LifecycleStateCounts { get; init; } = [];
}

/// <summary>
/// Aggregate view over a batch resource version history result.
/// </summary>
public sealed record ResourceVersionHistoryBatchSummary
{
    /// <summary>Effective tenant used by the source batch result.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Number of resource histories in the batch result.</summary>
    public required int SelectedResourceCount { get; init; }

    /// <summary>Number of resource histories containing one or more versions.</summary>
    public required int ResourcesWithVersionsCount { get; init; }

    /// <summary>Number of resource histories containing no versions.</summary>
    public required int MissingResourceCount { get; init; }

    /// <summary>Total number of version summaries across all histories.</summary>
    public required int TotalVersionCount { get; init; }

    /// <summary>Number of version summaries active in at least one channel.</summary>
    public required int ActiveVersionCount { get; init; }

    /// <summary>Number of version summaries protected from destructive pruning.</summary>
    public required int ProtectedVersionCount { get; init; }

    /// <summary>Number of version summaries with possible-candidate maintenance disposition.</summary>
    public required int PossibleCandidateCount { get; init; }

    /// <summary>Deterministic lifecycle marker state counts across all version summaries.</summary>
    public IReadOnlyList<ResourceVersionLifecycleStateCount> LifecycleStateCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for resource version history result objects.
/// </summary>
public static class ResourceVersionHistorySummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for one resource version history result.
    /// </summary>
    /// <param name="result">The history result to summarize.</param>
    /// <returns>A summary over the history's version states.</returns>
    public static ResourceVersionHistorySummary ToSummary(this ResourceVersionHistoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var versions = result.Versions ?? [];

        return new ResourceVersionHistorySummary
        {
            TenantScope = result.TenantScope,
            ResourceId = result.ResourceId,
            TotalVersionCount = versions.Count,
            LatestVersionCount = versions.Count(static version => version.IsLatest),
            DraftVersionCount = versions.Count(static version => version.IsDraft),
            ActiveVersionCount = versions.Count(static version => version.ActiveChannels is { Count: > 0 }),
            ProtectedVersionCount = versions.Count(static version => version.IsProtectedFromPruning),
            PossibleCandidateCount = versions.Count(static version => version.MaintenanceDisposition == ResourceVersionMaintenanceDisposition.PossibleCandidate),
            LifecycleStateCounts = CountLifecycleStates(versions),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for a batch resource version history result.
    /// </summary>
    /// <param name="result">The batch history result to summarize.</param>
    /// <returns>A summary over the batch's resource and version states.</returns>
    public static ResourceVersionHistoryBatchSummary ToSummary(this ResourceVersionHistoryBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var histories = result.Histories ?? [];
        var versionSets = histories.Select(static history => history.Versions ?? []).ToList();
        var versions = versionSets.SelectMany(static historyVersions => historyVersions).ToList();

        return new ResourceVersionHistoryBatchSummary
        {
            TenantScope = result.TenantScope,
            SelectedResourceCount = histories.Count,
            ResourcesWithVersionsCount = versionSets.Count(static historyVersions => historyVersions.Count > 0),
            MissingResourceCount = versionSets.Count(static historyVersions => historyVersions.Count == 0),
            TotalVersionCount = versions.Count,
            ActiveVersionCount = versions.Count(static version => version.ActiveChannels is { Count: > 0 }),
            ProtectedVersionCount = versions.Count(static version => version.IsProtectedFromPruning),
            PossibleCandidateCount = versions.Count(static version => version.MaintenanceDisposition == ResourceVersionMaintenanceDisposition.PossibleCandidate),
            LifecycleStateCounts = CountLifecycleStates(versions),
        };
    }

    private static IReadOnlyList<ResourceVersionLifecycleStateCount> CountLifecycleStates(
        IEnumerable<ResourceVersionSummary> versions) =>
        versions
            .GroupBy(static version => version.LifecycleState)
            .OrderBy(static group => group.Key)
            .Select(static group => new ResourceVersionLifecycleStateCount
            {
                State = group.Key,
                Count = group.Count(),
            })
            .ToList();
}
