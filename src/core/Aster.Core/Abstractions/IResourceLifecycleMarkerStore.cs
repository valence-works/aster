using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Provider-facing storage for explicit resource lifecycle markers.
/// </summary>
public interface IResourceLifecycleMarkerStore
{
    /// <summary>
    /// Reads the effective lifecycle marker for a resource.
    /// </summary>
    ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads effective lifecycle markers for the supplied resources.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the effective lifecycle marker for a resource.
    /// </summary>
    ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
        ResourceLifecycleMarker marker,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-facing storage capability for removing explicit lifecycle markers.
/// </summary>
public interface IResourceLifecycleMarkerClearStore : IResourceLifecycleMarkerStore
{
    /// <summary>
    /// Removes the effective lifecycle marker for a resource in the supplied tenant when its state matches the expected state.
    /// </summary>
    /// <returns><see langword="true" /> when a matching marker existed and was removed; otherwise <see langword="false" />.</returns>
    ValueTask<bool> ClearMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        ResourceLifecycleMarkerState expectedState,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-facing service for explicit archive and soft-delete marker writes.
/// </summary>
public interface IResourceLifecycleMarkerService
{
    /// <summary>
    /// Applies an archive or soft-delete marker to a resource.
    /// </summary>
    ValueTask<ResourceLifecycleMarkerResult> ApplyAsync(
        ResourceLifecycleMarkerRequest request,
        CancellationToken cancellationToken = default);
}
