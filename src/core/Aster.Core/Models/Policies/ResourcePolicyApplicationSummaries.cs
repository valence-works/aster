namespace Aster.Core.Models.Policies;

/// <summary>
/// Deterministic count for one policy diagnostic code.
/// </summary>
public sealed record ResourcePolicyDiagnosticCodeCount
{
    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of diagnostics with the code.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over a marker-based policy application result.
/// </summary>
public sealed record ResourcePolicyApplicationSummary
{
    /// <summary>Total number of candidate results.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Number of candidates that wrote a lifecycle marker.</summary>
    public required int AppliedCount { get; init; }

    /// <summary>Number of candidates already satisfied by current marker state.</summary>
    public required int AlreadySatisfiedCount { get; init; }

    /// <summary>Number of candidates skipped deterministically without writes.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>Number of candidates that failed.</summary>
    public required int FailedCount { get; init; }

    /// <summary>Whether one or more candidates failed.</summary>
    public bool HasFailures => FailedCount > 0;

    /// <summary>Whether every candidate completed through a successful terminal status.</summary>
    public bool IsFullySuccessful => TotalCount == AppliedCount + AlreadySatisfiedCount;

    /// <summary>Number of distinct resource identifiers affected by successful candidates.</summary>
    public required int AffectedResourceCount { get; init; }

    /// <summary>Deterministic diagnostic code counts across candidate diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Aggregate view over a policy pruning application result.
/// </summary>
public sealed record ResourcePolicyPruningApplicationSummary
{
    /// <summary>Total number of candidate results.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Number of candidates that removed a resource version.</summary>
    public required int PrunedCount { get; init; }

    /// <summary>Number of candidates whose target version was already absent.</summary>
    public required int AlreadyPrunedCount { get; init; }

    /// <summary>Number of duplicate candidates skipped deterministically.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>Number of candidates that failed.</summary>
    public required int FailedCount { get; init; }

    /// <summary>Whether one or more candidates failed.</summary>
    public bool HasFailures => FailedCount > 0;

    /// <summary>Whether every candidate completed through a successful terminal status.</summary>
    public bool IsFullySuccessful => TotalCount == PrunedCount + AlreadyPrunedCount;

    /// <summary>Number of distinct resource/version targets affected by successful candidates.</summary>
    public required int AffectedTargetCount { get; init; }

    /// <summary>Deterministic diagnostic code counts across candidate diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for policy application result objects.
/// </summary>
public static class ResourcePolicyApplicationSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for a marker-based policy application result.
    /// </summary>
    /// <param name="result">The result to summarize.</param>
    /// <returns>A summary over the result's candidate statuses and diagnostics.</returns>
    public static ResourcePolicyApplicationSummary ToSummary(this ResourcePolicyApplicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var candidates = result.Candidates ?? [];

        return new ResourcePolicyApplicationSummary
        {
            TotalCount = candidates.Count,
            AppliedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.Applied),
            AlreadySatisfiedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.AlreadySatisfied),
            SkippedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.Skipped),
            FailedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.Failed),
            AffectedResourceCount = candidates
                .Where(static candidate => candidate.Status is ResourcePolicyApplicationCandidateStatus.Applied or ResourcePolicyApplicationCandidateStatus.AlreadySatisfied)
                .Select(static candidate => candidate.ResourceId)
                .Where(static resourceId => !string.IsNullOrWhiteSpace(resourceId))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            DiagnosticCodeCounts = CountDiagnosticCodes(candidates.SelectMany(static candidate => candidate.Diagnostics ?? [])),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for a policy pruning application result.
    /// </summary>
    /// <param name="result">The result to summarize.</param>
    /// <returns>A summary over the result's candidate statuses and diagnostics.</returns>
    public static ResourcePolicyPruningApplicationSummary ToSummary(this ResourcePolicyPruningApplicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var candidates = result.Candidates ?? [];

        return new ResourcePolicyPruningApplicationSummary
        {
            TotalCount = candidates.Count,
            PrunedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyPruningApplicationCandidateStatus.Pruned),
            AlreadyPrunedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned),
            SkippedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyPruningApplicationCandidateStatus.Skipped),
            FailedCount = candidates.Count(static candidate => candidate.Status == ResourcePolicyPruningApplicationCandidateStatus.Failed),
            AffectedTargetCount = candidates
                .Where(static candidate => candidate.Status is ResourcePolicyPruningApplicationCandidateStatus.Pruned or ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned)
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.ResourceId) && candidate.ResourceVersion.HasValue)
                .Select(static candidate => (candidate.ResourceId!, candidate.ResourceVersion!.Value))
                .Distinct()
                .Count(),
            DiagnosticCodeCounts = CountDiagnosticCodes(candidates.SelectMany(static candidate => candidate.Diagnostics ?? [])),
        };
    }

    private static IReadOnlyList<ResourcePolicyDiagnosticCodeCount> CountDiagnosticCodes(
        IEnumerable<ResourcePolicyDiagnostic> diagnostics) =>
        diagnostics
            .Select(static diagnostic => diagnostic.Code)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .GroupBy(static code => code, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new ResourcePolicyDiagnosticCodeCount
            {
                Code = group.Key,
                Count = group.Count(),
            })
            .ToList();
}
