using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Request to inspect one resource's version history.
/// </summary>
public sealed class ResourceVersionHistoryRequest
{
    /// <summary>
    /// Tenant scope for the read. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>
    /// Logical resource identifier.
    /// </summary>
    public string? ResourceId { get; set; }
}

/// <summary>
/// Request to inspect multiple resources' version histories in one tenant.
/// </summary>
public sealed class ResourceVersionHistoryBatchRequest
{
    /// <summary>
    /// Tenant scope for the read. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>
    /// Logical resource identifiers to inspect.
    /// </summary>
    public IReadOnlyCollection<string>? ResourceIds { get; set; }
}

/// <summary>
/// Read-only version histories for an explicit resource selection.
/// </summary>
public sealed record ResourceVersionHistoryBatchResult
{
    /// <summary>
    /// Effective tenant used for all reads.
    /// </summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>
    /// Ordered histories for each distinct requested logical resource identifier.
    /// </summary>
    public IReadOnlyList<ResourceVersionHistoryResult> Histories { get; init; } = [];
}

/// <summary>
/// Read-only version history for one resource.
/// </summary>
public sealed record ResourceVersionHistoryResult
{
    /// <summary>
    /// Effective tenant used for all reads.
    /// </summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>
    /// Requested logical resource identifier.
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Ordered version summaries.
    /// </summary>
    public IReadOnlyList<ResourceVersionSummary> Versions { get; init; } = [];
}

/// <summary>
/// Read-only summary of one resource version.
/// </summary>
public sealed record ResourceVersionSummary
{
    /// <summary>
    /// Version snapshot identifier.
    /// </summary>
    public required string ResourceVersionId { get; init; }

    /// <summary>
    /// Resource version number.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Logical resource definition identifier.
    /// </summary>
    public required string DefinitionId { get; init; }

    /// <summary>
    /// Resource definition version captured by this resource version.
    /// </summary>
    public int? DefinitionVersion { get; init; }

    /// <summary>
    /// Version creation timestamp.
    /// </summary>
    public required DateTime Created { get; init; }

    /// <summary>
    /// Whether this is the current latest version.
    /// </summary>
    public required bool IsLatest { get; init; }

    /// <summary>
    /// Whether this version is absent from all active channels.
    /// </summary>
    public required bool IsDraft { get; init; }

    /// <summary>
    /// Active channel names containing this version.
    /// </summary>
    public IReadOnlyList<string> ActiveChannels { get; init; } = [];

    /// <summary>
    /// Current lifecycle marker state for the logical resource.
    /// </summary>
    public ResourceLifecycleMarkerState LifecycleState { get; init; } = ResourceLifecycleMarkerState.None;

    /// <summary>
    /// Whether destructive pruning must protect this version.
    /// </summary>
    public required bool IsProtectedFromPruning { get; init; }

    /// <summary>
    /// Conservative maintenance hint for host displays.
    /// </summary>
    public required ResourceVersionMaintenanceDisposition MaintenanceDisposition { get; init; }
}

/// <summary>
/// Conservative maintenance signal for a resource version.
/// </summary>
public enum ResourceVersionMaintenanceDisposition
{
    /// <summary>
    /// Version is latest or active and must be protected from destructive pruning.
    /// </summary>
    Protected,

    /// <summary>
    /// Version is historical, inactive, and not latest. Policy evaluation is still required before pruning.
    /// </summary>
    PossibleCandidate,
}
