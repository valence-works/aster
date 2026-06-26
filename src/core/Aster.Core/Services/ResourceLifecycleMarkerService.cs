using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;

namespace Aster.Core.Services;

/// <summary>
/// Default host-facing lifecycle marker service.
/// </summary>
public sealed class ResourceLifecycleMarkerService : IResourceLifecycleMarkerService
{
    private readonly IResourceLifecycleMarkerTransitionService transitions;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleMarkerService"/>.
    /// </summary>
    public ResourceLifecycleMarkerService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
        : this(new ResourceLifecycleMarkerTransitionService(versionReader, markerStore))
    {
    }

    internal ResourceLifecycleMarkerService(IResourceLifecycleMarkerTransitionService transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        this.transitions = transitions;
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
