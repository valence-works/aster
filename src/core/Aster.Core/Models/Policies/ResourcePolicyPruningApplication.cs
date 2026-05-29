using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Policies;

/// <summary>
/// Host-controlled request to apply selected version-pruning preview outcomes.
/// </summary>
public sealed class ResourcePolicyPruningApplicationRequest
{
    /// <summary>Tenant scope for pruning. When omitted, the default single-tenant scope is used.</summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>Selected candidates to apply, preserved in result order.</summary>
    public List<ResourcePolicyPruningApplicationCandidate> Candidates { get; set; } = [];

    /// <summary>Optional host timestamp for application reporting and age-based policy criteria checks.</summary>
    public DateTimeOffset? AppliedAt { get; set; }
}

/// <summary>
/// One selected version-pruning preview candidate to apply.
/// </summary>
public sealed class ResourcePolicyPruningApplicationCandidate
{
    /// <summary>Policy declaration identifier from the preview candidate.</summary>
    public string? PolicyId { get; set; }

    /// <summary>Policy kind from the preview candidate.</summary>
    public ResourcePolicyKind? PolicyKind { get; set; }

    /// <summary>Previewed outcome to apply.</summary>
    public ResourcePolicyOutcome? Outcome { get; set; }

    /// <summary>Target logical resource identifier.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Previewed resource version.</summary>
    public int? ResourceVersion { get; set; }
}

/// <summary>
/// Overall policy pruning application result.
/// </summary>
public sealed record ResourcePolicyPruningApplicationResult
{
    /// <summary>Effective tenant used by the request.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Optional host timestamp supplied by the request.</summary>
    public DateTimeOffset? AppliedAt { get; init; }

    /// <summary>Per-candidate results in input order.</summary>
    public IReadOnlyList<ResourcePolicyPruningApplicationCandidateResult> Candidates { get; init; } = [];

    /// <summary>Number of candidates that removed a version.</summary>
    public int PrunedCount => Count(ResourcePolicyPruningApplicationCandidateStatus.Pruned);

    /// <summary>Number of candidates whose target version was already absent.</summary>
    public int AlreadyPrunedCount => Count(ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned);

    /// <summary>Number of duplicate candidates skipped deterministically.</summary>
    public int SkippedCount => Count(ResourcePolicyPruningApplicationCandidateStatus.Skipped);

    /// <summary>Number of candidates that failed preflight or removal.</summary>
    public int FailedCount => Count(ResourcePolicyPruningApplicationCandidateStatus.Failed);

    private int Count(ResourcePolicyPruningApplicationCandidateStatus status) =>
        Candidates.Count(candidate => candidate.Status == status);
}

/// <summary>
/// Per-candidate pruning application status.
/// </summary>
public enum ResourcePolicyPruningApplicationCandidateStatus
{
    /// <summary>The target version was removed.</summary>
    Pruned,

    /// <summary>The target resource exists and the submitted version is already absent.</summary>
    AlreadyPruned,

    /// <summary>A prior duplicate candidate already determined the outcome.</summary>
    Skipped,

    /// <summary>The candidate failed validation, preflight, or removal.</summary>
    Failed,
}

/// <summary>
/// Result for one submitted policy pruning candidate.
/// </summary>
public sealed record ResourcePolicyPruningApplicationCandidateResult
{
    /// <summary>Zero-based input candidate index.</summary>
    public required int Index { get; init; }

    /// <summary>Per-candidate status.</summary>
    public required ResourcePolicyPruningApplicationCandidateStatus Status { get; init; }

    /// <summary>Policy identifier when supplied.</summary>
    public string? PolicyId { get; init; }

    /// <summary>Target resource identifier when supplied.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Target resource version when supplied.</summary>
    public int? ResourceVersion { get; init; }

    /// <summary>Stable diagnostics for failed candidates.</summary>
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}
