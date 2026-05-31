using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Policies;

/// <summary>
/// Deterministic count for one policy preview outcome.
/// </summary>
public sealed record ResourcePolicyOutcomeCount
{
    /// <summary>Previewed policy outcome.</summary>
    public required ResourcePolicyOutcome Outcome { get; init; }

    /// <summary>Number of candidates with the outcome.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one policy kind.
/// </summary>
public sealed record ResourcePolicyKindCount
{
    /// <summary>Policy kind.</summary>
    public required ResourcePolicyKind Kind { get; init; }

    /// <summary>Number of candidates with the kind.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over a policy evaluation preview result.
/// </summary>
public sealed record ResourcePolicyPreviewSummary
{
    /// <summary>Effective tenant used by the preview result.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Timestamp used by the preview result when supplied.</summary>
    public DateTimeOffset? EvaluationTimestamp { get; init; }

    /// <summary>Total number of preview candidate outcomes.</summary>
    public required int TotalCandidateCount { get; init; }

    /// <summary>Number of distinct nonblank logical resources represented by preview candidates.</summary>
    public required int DistinctResourceCount { get; init; }

    /// <summary>Number of distinct resource/version targets represented by preview candidates.</summary>
    public required int DistinctResourceVersionTargetCount { get; init; }

    /// <summary>Whether one or more nonblank diagnostic codes are present.</summary>
    public bool HasDiagnostics => DiagnosticCodeCounts.Count > 0;

    /// <summary>Whether no nonblank diagnostic codes are present.</summary>
    public bool IsDiagnosticFree => !HasDiagnostics;

    /// <summary>Deterministic policy outcome counts across preview candidates.</summary>
    public IReadOnlyList<ResourcePolicyOutcomeCount> OutcomeCounts { get; init; } = [];

    /// <summary>Deterministic policy kind counts across preview candidates.</summary>
    public IReadOnlyList<ResourcePolicyKindCount> KindCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic code counts across preview diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for policy evaluation preview result objects.
/// </summary>
public static class ResourcePolicyPreviewSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for a policy evaluation preview result.
    /// </summary>
    /// <param name="preview">The preview result to summarize.</param>
    /// <returns>A summary over the preview's candidates and diagnostics.</returns>
    public static ResourcePolicyPreviewSummary ToSummary(this ResourcePolicyEvaluationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var candidates = preview.Candidates ?? [];
        var diagnosticCodeCounts = ResourcePolicyDiagnosticCodeCounter.Count(preview.Diagnostics ?? []);

        return new ResourcePolicyPreviewSummary
        {
            TenantScope = preview.TenantScope,
            EvaluationTimestamp = preview.EvaluationTimestamp,
            TotalCandidateCount = candidates.Count,
            DistinctResourceCount = candidates
                .Select(static candidate => candidate.ResourceId)
                .Where(static resourceId => !string.IsNullOrWhiteSpace(resourceId))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            DistinctResourceVersionTargetCount = candidates
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.ResourceId) && candidate.ResourceVersion.HasValue)
                .Select(static candidate => (candidate.ResourceId, candidate.ResourceVersion!.Value))
                .Distinct()
                .Count(),
            OutcomeCounts = candidates
                .GroupBy(static candidate => candidate.Outcome)
                .OrderBy(static group => group.Key)
                .Select(static group => new ResourcePolicyOutcomeCount
                {
                    Outcome = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            KindCounts = candidates
                .GroupBy(static candidate => candidate.PolicyKind)
                .OrderBy(static group => group.Key)
                .Select(static group => new ResourcePolicyKindCount
                {
                    Kind = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            DiagnosticCodeCounts = diagnosticCodeCounts,
        };
    }
}
