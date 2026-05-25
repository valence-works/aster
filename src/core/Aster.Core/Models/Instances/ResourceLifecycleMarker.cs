using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Current lifecycle marker state for a logical resource.
/// </summary>
public sealed record ResourceLifecycleMarker
{
    /// <summary>Tenant scope that owns this marker.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Logical resource identifier.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Current effective lifecycle marker state.</summary>
    public required ResourceLifecycleMarkerState State { get; init; }

    /// <summary>Host-supplied marker timestamp.</summary>
    public required DateTimeOffset MarkedAt { get; init; }

    /// <summary>Optional host-visible marker reason.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Lifecycle marker states supported by this slice.
/// </summary>
public enum ResourceLifecycleMarkerState
{
    /// <summary>No archive or soft-delete marker is present.</summary>
    None,

    /// <summary>The resource is archived.</summary>
    Archived,

    /// <summary>The resource is soft-deleted.</summary>
    SoftDeleted,
}

/// <summary>
/// Explicit request to apply a lifecycle marker to a resource.
/// </summary>
public sealed class ResourceLifecycleMarkerRequest
{
    /// <summary>Tenant scope for the marker write. When omitted, the default single-tenant scope is used.</summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>Logical resource identifier.</summary>
    public required string ResourceId { get; set; }

    /// <summary>Lifecycle state to apply.</summary>
    public required ResourceLifecycleMarkerState State { get; set; }

    /// <summary>Host-supplied marker timestamp.</summary>
    public required DateTimeOffset MarkedAt { get; set; }

    /// <summary>Optional host-visible reason.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Result of an explicit lifecycle marker write.
/// </summary>
public sealed record ResourceLifecycleMarkerResult
{
    /// <summary>Whether the marker request succeeded.</summary>
    public bool Succeeded => Diagnostics.Count == 0;

    /// <summary>Effective marker after the operation when available.</summary>
    public ResourceLifecycleMarker? Marker { get; init; }

    /// <summary>Stable marker diagnostics.</summary>
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}
