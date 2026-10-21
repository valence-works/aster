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
    /// Persists the effective lifecycle marker for a resource, replacing any marker it already has.
    /// </summary>
    ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
        ResourceLifecycleMarker marker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the lifecycle marker for a resource only when the resource has none in the marker's tenant. The check
    /// and the write are atomic, so of two concurrent calls for one resource exactly one creates its marker.
    /// </summary>
    /// <returns>The marker written, or the resource's existing marker when it already had one.</returns>
    ValueTask<ResourceLifecycleMarkerCreateResult> CreateMarkerAsync(
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
/// Host-facing service for explicit archive and soft-delete markers: reads and writes.
/// </summary>
public interface IResourceLifecycleMarkerService
{
    /// <summary>
    /// Reads a resource's lifecycle marker: its state, reason, and marked-at time.
    /// </summary>
    /// <param name="resourceId">The logical resource ID.</param>
    /// <param name="tenantScope">The tenant to read in; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The marker, or <see langword="null"/> when the resource has none in that tenant.</returns>
    ValueTask<ResourceLifecycleMarker?> GetAsync(
        string resourceId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an archive or soft-delete marker to a resource.
    /// </summary>
    ValueTask<ResourceLifecycleMarkerResult> ApplyAsync(
        ResourceLifecycleMarkerRequest request,
        CancellationToken cancellationToken = default);
}
