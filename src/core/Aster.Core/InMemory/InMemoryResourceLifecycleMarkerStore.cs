using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Core.InMemory;

/// <summary>
/// In-memory lifecycle marker storage.
/// </summary>
public sealed class InMemoryResourceLifecycleMarkerStore : IResourceLifecycleMarkerClearStore
{
    private readonly ConcurrentDictionary<(string TenantId, string ResourceId), ResourceLifecycleMarker> markers = [];

    /// <inheritdoc />
    public ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        markers.TryGetValue((tenant.TenantId, resourceId), out var marker);
        return ValueTask.FromResult(marker);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var results = new Dictionary<string, ResourceLifecycleMarker>(StringComparer.Ordinal);

        foreach (var resourceId in resourceIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (markers.TryGetValue((tenant.TenantId, resourceId), out var marker))
                results[resourceId] = marker;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, ResourceLifecycleMarker>>(results);
    }

    /// <inheritdoc />
    public ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
        ResourceLifecycleMarker marker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker.ResourceId);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = TenantScopeResolver.Resolve(marker.TenantScope);
        var scopedMarker = marker with { TenantScope = tenant };
        markers[(tenant.TenantId, scopedMarker.ResourceId)] = scopedMarker;
        return ValueTask.FromResult(scopedMarker);
    }

    /// <inheritdoc />
    public ValueTask<bool> ClearMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        ResourceLifecycleMarkerState expectedState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var key = (tenant.TenantId, resourceId);

        while (markers.TryGetValue(key, out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.State != expectedState)
                return ValueTask.FromResult(false);

            var removed = ((ICollection<KeyValuePair<(string TenantId, string ResourceId), ResourceLifecycleMarker>>)markers)
                .Remove(new KeyValuePair<(string TenantId, string ResourceId), ResourceLifecycleMarker>(key, current));
            if (removed)
                return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }

    internal void RestoreMarker(ResourceLifecycleMarker marker) =>
        markers[(marker.TenantScope.TenantId, marker.ResourceId)] = marker;

    internal void RemoveMarker(TenantScope tenantScope, string resourceId)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        markers.TryRemove((tenant.TenantId, resourceId), out _);
    }
}
