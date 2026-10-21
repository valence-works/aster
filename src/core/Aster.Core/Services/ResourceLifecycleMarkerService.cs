using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default host-facing lifecycle marker service.
/// </summary>
internal sealed class ResourceLifecycleMarkerService : IResourceLifecycleMarkerService
{
    private readonly IResourceLifecycleMarkerTransitionService transitions;
    private readonly IResourceLifecycleMarkerStore markerStore;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleMarkerService"/>.
    /// </summary>
    public ResourceLifecycleMarkerService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
        : this(new ResourceLifecycleMarkerTransitionService(versionReader, markerStore), markerStore)
    {
    }

    internal ResourceLifecycleMarkerService(
        IResourceLifecycleMarkerTransitionService transitions,
        IResourceLifecycleMarkerStore markerStore)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(markerStore);
        this.transitions = transitions;
        this.markerStore = markerStore;
    }

    /// <inheritdoc />
    public ValueTask<ResourceLifecycleMarker?> GetAsync(
        string resourceId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return markerStore.GetMarkerAsync(resourceId, TenantScopeResolver.Resolve(tenantScope), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceLifecycleMarkerResult> ApplyAsync(
        ResourceLifecycleMarkerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceId);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        var transition = await transitions.ApplyAsync(new ResourceLifecycleMarkerTransitionApplyRequest
        {
            TenantScope = tenant,
            ResourceId = request.ResourceId,
            State = request.State,
            MarkedAt = request.MarkedAt,
            Reason = request.Reason,
        }, cancellationToken);

        return new ResourceLifecycleMarkerResult
        {
            Marker = transition.Marker,
            Diagnostics = transition.Diagnostics,
        };
    }
}
