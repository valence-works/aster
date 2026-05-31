using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Aggregate view over a lifecycle restore application result.
/// </summary>
public sealed record ResourceLifecycleRestoreApplicationSummary
{
    /// <summary>Effective tenant used by the application result.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Optional host timestamp supplied by the application result.</summary>
    public DateTimeOffset? RestoredAt { get; init; }

    /// <summary>Total number of candidate results.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Number of candidates that cleared a matching lifecycle marker.</summary>
    public required int RestoredCount { get; init; }

    /// <summary>Number of candidates already in the restored state.</summary>
    public required int AlreadyRestoredCount { get; init; }

    /// <summary>Number of duplicate candidates skipped deterministically.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>Number of candidates that failed.</summary>
    public required int FailedCount { get; init; }

    /// <summary>Whether one or more candidates failed.</summary>
    public bool HasFailures => FailedCount > 0;

    /// <summary>Whether every candidate completed through a successful terminal application status.</summary>
    public bool IsFullySuccessful => TotalCount == RestoredCount + AlreadyRestoredCount;

    /// <summary>Number of distinct resource identifiers affected by successful candidates.</summary>
    public required int AffectedResourceCount { get; init; }

    /// <summary>Deterministic diagnostic code counts across candidate diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Aggregate view over a lifecycle restore preview result.
/// </summary>
public sealed record ResourceLifecycleRestorePreviewSummary
{
    /// <summary>Effective tenant used by the preview result.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Total number of candidate results.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Number of candidates that can be restored.</summary>
    public required int RestorableCount { get; init; }

    /// <summary>Number of candidates already in the restored state.</summary>
    public required int AlreadyRestoredCount { get; init; }

    /// <summary>Number of duplicate candidates skipped deterministically.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>Number of candidates that failed.</summary>
    public required int FailedCount { get; init; }

    /// <summary>Whether one or more candidates failed.</summary>
    public bool HasFailures => FailedCount > 0;

    /// <summary>Whether every candidate completed through a successful terminal preview status.</summary>
    public bool IsFullySuccessful => TotalCount == RestorableCount + AlreadyRestoredCount;

    /// <summary>Number of distinct resource identifiers represented by successful preview candidates.</summary>
    public required int CandidateResourceCount { get; init; }

    /// <summary>Deterministic diagnostic code counts across candidate diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for lifecycle restore result objects.
/// </summary>
public static class ResourceLifecycleRestoreSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for a lifecycle restore application result.
    /// </summary>
    /// <param name="result">The result to summarize.</param>
    /// <returns>A summary over the result's candidate statuses and diagnostics.</returns>
    public static ResourceLifecycleRestoreApplicationSummary ToSummary(this ResourceLifecycleRestoreApplicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var candidates = result.Candidates ?? [];

        return new ResourceLifecycleRestoreApplicationSummary
        {
            TenantScope = result.TenantScope,
            RestoredAt = result.RestoredAt,
            TotalCount = candidates.Count,
            RestoredCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.Restored),
            AlreadyRestoredCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.AlreadyRestored),
            SkippedCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.Skipped),
            FailedCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.Failed),
            AffectedResourceCount = CountDistinctResources(
                candidates,
                ResourceLifecycleRestoreCandidateStatus.Restored,
                ResourceLifecycleRestoreCandidateStatus.AlreadyRestored),
            DiagnosticCodeCounts = ResourcePolicyDiagnosticCodeCounter.Count(candidates.SelectMany(static candidate => candidate.Diagnostics ?? [])),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for a lifecycle restore preview result.
    /// </summary>
    /// <param name="result">The result to summarize.</param>
    /// <returns>A summary over the result's candidate statuses and diagnostics.</returns>
    public static ResourceLifecycleRestorePreviewSummary ToSummary(this ResourceLifecycleRestorePreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var candidates = result.Candidates ?? [];

        return new ResourceLifecycleRestorePreviewSummary
        {
            TenantScope = result.TenantScope,
            TotalCount = candidates.Count,
            RestorableCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.Restorable),
            AlreadyRestoredCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.AlreadyRestored),
            SkippedCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.Skipped),
            FailedCount = candidates.Count(static candidate => candidate.Status == ResourceLifecycleRestoreCandidateStatus.Failed),
            CandidateResourceCount = CountDistinctResources(
                candidates,
                ResourceLifecycleRestoreCandidateStatus.Restorable,
                ResourceLifecycleRestoreCandidateStatus.AlreadyRestored),
            DiagnosticCodeCounts = ResourcePolicyDiagnosticCodeCounter.Count(candidates.SelectMany(static candidate => candidate.Diagnostics ?? [])),
        };
    }

    private static int CountDistinctResources(
        IEnumerable<ResourceLifecycleRestoreCandidateResult> candidates,
        params ResourceLifecycleRestoreCandidateStatus[] statuses) =>
        candidates
            .Where(candidate => statuses.Contains(candidate.Status))
            .Select(static candidate => candidate.ResourceId)
            .Where(static resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.Ordinal)
            .Count();
}
