using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Host-provided lifecycle restore request.
/// </summary>
public sealed class ResourceLifecycleRestoreRequest
{
    /// <summary>Tenant scope for restore. When omitted, the default single-tenant scope is used.</summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>Selected restore candidates in input order.</summary>
    public List<ResourceLifecycleRestoreCandidate> Candidates { get; set; } = [];

    /// <summary>Optional host timestamp for application reporting.</summary>
    public DateTimeOffset? RestoredAt { get; set; }
}

/// <summary>
/// Host-selected lifecycle marker restore target.
/// </summary>
public sealed class ResourceLifecycleRestoreCandidate
{
    /// <summary>Logical resource identifier.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Lifecycle marker state expected to be cleared.</summary>
    public ResourceLifecycleMarkerState? ExpectedState { get; set; }
}

/// <summary>
/// Non-mutating lifecycle restore preview result.
/// </summary>
public sealed record ResourceLifecycleRestorePreviewResult
{
    /// <summary>Effective tenant used for preview.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Ordered preview candidate results.</summary>
    public IReadOnlyList<ResourceLifecycleRestoreCandidateResult> Candidates { get; init; } = [];

    /// <summary>Number of candidates that can be restored.</summary>
    public int RestorableCount => Count(ResourceLifecycleRestoreCandidateStatus.Restorable);

    /// <summary>Number of candidates already in the restored state.</summary>
    public int AlreadyRestoredCount => Count(ResourceLifecycleRestoreCandidateStatus.AlreadyRestored);

    /// <summary>Number of candidates skipped deterministically.</summary>
    public int SkippedCount => Count(ResourceLifecycleRestoreCandidateStatus.Skipped);

    /// <summary>Number of candidates that failed validation or state checks.</summary>
    public int FailedCount => Count(ResourceLifecycleRestoreCandidateStatus.Failed);

    private int Count(ResourceLifecycleRestoreCandidateStatus status) =>
        Candidates.Count(candidate => candidate.Status == status);
}

/// <summary>
/// Write-side lifecycle restore application result.
/// </summary>
public sealed record ResourceLifecycleRestoreApplicationResult
{
    /// <summary>Effective tenant used for application.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Optional host timestamp for application reporting.</summary>
    public DateTimeOffset? RestoredAt { get; init; }

    /// <summary>Ordered application candidate results.</summary>
    public IReadOnlyList<ResourceLifecycleRestoreCandidateResult> Candidates { get; init; } = [];

    /// <summary>Number of candidates restored by this application request.</summary>
    public int RestoredCount => Count(ResourceLifecycleRestoreCandidateStatus.Restored);

    /// <summary>Number of candidates already in the restored state.</summary>
    public int AlreadyRestoredCount => Count(ResourceLifecycleRestoreCandidateStatus.AlreadyRestored);

    /// <summary>Number of candidates skipped deterministically.</summary>
    public int SkippedCount => Count(ResourceLifecycleRestoreCandidateStatus.Skipped);

    /// <summary>Number of candidates that failed validation or state checks.</summary>
    public int FailedCount => Count(ResourceLifecycleRestoreCandidateStatus.Failed);

    private int Count(ResourceLifecycleRestoreCandidateStatus status) =>
        Candidates.Count(candidate => candidate.Status == status);
}

/// <summary>
/// Per-candidate lifecycle restore outcome.
/// </summary>
public sealed record ResourceLifecycleRestoreCandidateResult
{
    /// <summary>Zero-based input index.</summary>
    public required int Index { get; init; }

    /// <summary>Input resource identifier when available.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Expected marker state when supplied.</summary>
    public ResourceLifecycleMarkerState? ExpectedState { get; init; }

    /// <summary>Candidate outcome status.</summary>
    public required ResourceLifecycleRestoreCandidateStatus Status { get; init; }

    /// <summary>Marker observed before the candidate outcome, when available.</summary>
    public ResourceLifecycleMarker? Marker { get; init; }

    /// <summary>Stable diagnostics for failed or skipped candidates.</summary>
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Lifecycle restore candidate status.
/// </summary>
public enum ResourceLifecycleRestoreCandidateStatus
{
    /// <summary>Preview found a matching marker that can be restored.</summary>
    Restorable,

    /// <summary>Application cleared a matching marker.</summary>
    Restored,

    /// <summary>The target resource exists and no marker is present.</summary>
    AlreadyRestored,

    /// <summary>A prior duplicate candidate already determined the outcome.</summary>
    Skipped,

    /// <summary>The candidate failed validation or current-state checks.</summary>
    Failed,
}
