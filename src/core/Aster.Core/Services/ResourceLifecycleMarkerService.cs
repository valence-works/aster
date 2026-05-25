using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;

namespace Aster.Core.Services;

/// <summary>
/// Default host-facing lifecycle marker service.
/// </summary>
public sealed class ResourceLifecycleMarkerService : IResourceLifecycleMarkerService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleMarkerService"/>.
    /// </summary>
    public ResourceLifecycleMarkerService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceLifecycleMarkerResult> ApplyAsync(
        ResourceLifecycleMarkerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceId);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        if (request.State == ResourceLifecycleMarkerState.None || !Enum.IsDefined(request.State))
        {
            return Failure(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Lifecycle marker writes must apply Archived or SoftDeleted state.",
                request.ResourceId);
        }

        var resources = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.Latest,
        }, cancellationToken);

        if (!resources.Any(resource => string.Equals(resource.ResourceId, request.ResourceId, StringComparison.Ordinal)))
        {
            return Failure(
                ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound,
                $"Resource '{request.ResourceId}' was not found in tenant '{tenant.TenantId}'.",
                request.ResourceId);
        }

        var existing = await markerStore.GetMarkerAsync(request.ResourceId, tenant, cancellationToken);
        if (existing is not null)
        {
            if (existing.State == request.State)
                return new ResourceLifecycleMarkerResult { Marker = existing };

            return new ResourceLifecycleMarkerResult
            {
                Marker = existing,
                Diagnostics =
                [
                    ResourcePolicyValidator.Diagnostic(
                        ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict,
                        $"Resource '{request.ResourceId}' is already marked as {existing.State}.",
                        "state",
                        resourceId: request.ResourceId),
                ],
            };
        }

        var marker = await markerStore.SaveMarkerAsync(new ResourceLifecycleMarker
        {
            TenantScope = tenant,
            ResourceId = request.ResourceId,
            State = request.State,
            MarkedAt = request.MarkedAt,
            Reason = request.Reason,
        }, cancellationToken);

        return new ResourceLifecycleMarkerResult { Marker = marker };
    }

    private static ResourceLifecycleMarkerResult Failure(string code, string message, string resourceId) =>
        new()
        {
            Diagnostics =
            [
                ResourcePolicyValidator.Diagnostic(
                    code,
                    message,
                    resourceId: resourceId),
            ],
        };
}
