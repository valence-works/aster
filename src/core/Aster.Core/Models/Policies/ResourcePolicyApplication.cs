using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Policies;

/// <summary>
/// Host-controlled request to apply selected policy preview outcomes.
/// </summary>
public sealed class ResourcePolicyApplicationRequest
{
    /// <summary>Tenant scope for application. When omitted, the default single-tenant scope is used.</summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>Selected candidates to apply, preserved in result order.</summary>
    public List<ResourcePolicyApplicationCandidate> Candidates { get; set; } = [];

    /// <summary>Host-supplied timestamp for lifecycle marker writes.</summary>
    public required DateTimeOffset AppliedAt { get; set; }

    /// <summary>Optional host-visible default reason for marker writes.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// One selected policy preview candidate to apply.
/// </summary>
public sealed class ResourcePolicyApplicationCandidate
{
    /// <summary>Policy declaration identifier from the preview candidate.</summary>
    public string? PolicyId { get; set; }

    /// <summary>Policy kind from the preview candidate.</summary>
    public ResourcePolicyKind? PolicyKind { get; set; }

    /// <summary>Previewed outcome to apply.</summary>
    public ResourcePolicyOutcome? Outcome { get; set; }

    /// <summary>Target logical resource identifier.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Previewed resource version, when available.</summary>
    public int? ResourceVersion { get; set; }

    /// <summary>Optional host-visible marker reason override.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Overall policy application result.
/// </summary>
public sealed record ResourcePolicyApplicationResult
{
    /// <summary>Effective tenant used by the request.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Timestamp used for lifecycle marker writes.</summary>
    public required DateTimeOffset AppliedAt { get; init; }

    /// <summary>Per-candidate results in input order.</summary>
    public IReadOnlyList<ResourcePolicyApplicationCandidateResult> Candidates { get; init; } = [];

    /// <summary>Number of candidates that wrote a new marker.</summary>
    public int AppliedCount => Candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.Applied);

    /// <summary>Number of candidates already satisfied by existing marker state.</summary>
    public int AlreadySatisfiedCount => Candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.AlreadySatisfied);

    /// <summary>Number of candidates skipped deterministically without writes.</summary>
    public int SkippedCount => Candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.Skipped);

    /// <summary>Number of candidates that failed.</summary>
    public int FailedCount => Candidates.Count(static candidate => candidate.Status == ResourcePolicyApplicationCandidateStatus.Failed);
}

/// <summary>
/// Per-candidate application status.
/// </summary>
public enum ResourcePolicyApplicationCandidateStatus
{
    /// <summary>A new lifecycle marker was written.</summary>
    Applied,

    /// <summary>The requested lifecycle marker already existed.</summary>
    AlreadySatisfied,

    /// <summary>The candidate was deterministically skipped without failure.</summary>
    Skipped,

    /// <summary>The candidate failed and did not write a marker.</summary>
    Failed,
}

/// <summary>
/// Result for one submitted policy application candidate.
/// </summary>
public sealed record ResourcePolicyApplicationCandidateResult
{
    /// <summary>Zero-based input candidate index.</summary>
    public required int Index { get; init; }

    /// <summary>Per-candidate status.</summary>
    public required ResourcePolicyApplicationCandidateStatus Status { get; init; }

    /// <summary>Policy identifier when supplied.</summary>
    public string? PolicyId { get; init; }

    /// <summary>Outcome when supplied.</summary>
    public ResourcePolicyOutcome? Outcome { get; init; }

    /// <summary>Target resource identifier when supplied.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Target resource version when supplied.</summary>
    public int? ResourceVersion { get; init; }

    /// <summary>Effective lifecycle marker when application succeeded or was already satisfied.</summary>
    public ResourceLifecycleMarker? Marker { get; init; }

    /// <summary>Stable diagnostics for failed or skipped candidates.</summary>
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}
