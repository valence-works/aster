using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

internal interface IResourceLifecycleMarkerTransitionService
{
    ValueTask<ResourceLifecycleMarkerTransitionResult> ApplyAsync(
        ResourceLifecycleMarkerTransitionApplyRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ResourceLifecycleMarkerTransitionService : IResourceLifecycleMarkerTransitionService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;

    public ResourceLifecycleMarkerTransitionService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
    }

    public async ValueTask<ResourceLifecycleMarkerTransitionResult> ApplyAsync(
        ResourceLifecycleMarkerTransitionApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceId);

        if (request.State == ResourceLifecycleMarkerState.None || !Enum.IsDefined(request.State))
        {
            return Failure(
                ResourceLifecycleMarkerTransitionStatus.Failed,
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Lifecycle marker writes must apply Archived or SoftDeleted state.",
                request.ResourceId);
        }

        if (!await TargetExistsAsync(request, cancellationToken))
        {
            return Failure(
                ResourceLifecycleMarkerTransitionStatus.Failed,
                ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound,
                $"Resource '{request.ResourceId}' was not found in tenant '{request.TenantScope.TenantId}'.",
                request.ResourceId);
        }

        var existing = await ResolveCurrentMarkerAsync(request, cancellationToken);
        if (existing is not null)
        {
            if (existing.State == request.State)
            {
                return new ResourceLifecycleMarkerTransitionResult
                {
                    Status = ResourceLifecycleMarkerTransitionStatus.AlreadySatisfied,
                    Marker = existing,
                };
            }

            return new ResourceLifecycleMarkerTransitionResult
            {
                Status = ResourceLifecycleMarkerTransitionStatus.Failed,
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
            TenantScope = request.TenantScope,
            ResourceId = request.ResourceId,
            State = request.State,
            MarkedAt = request.MarkedAt,
            Reason = request.Reason,
        }, cancellationToken);

        return new ResourceLifecycleMarkerTransitionResult
        {
            Status = ResourceLifecycleMarkerTransitionStatus.Applied,
            Marker = marker,
        };
    }

    private async ValueTask<bool> TargetExistsAsync(
        ResourceLifecycleMarkerTransitionApplyRequest request,
        CancellationToken cancellationToken)
    {
        var resources = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = request.TenantScope,
            Scope = ResourceVersionScope.Latest,
            ResourceIds = [request.ResourceId],
        }, cancellationToken);

        return resources.Any(resource => string.Equals(resource.ResourceId, request.ResourceId, StringComparison.Ordinal));
    }

    private async ValueTask<ResourceLifecycleMarker?> ResolveCurrentMarkerAsync(
        ResourceLifecycleMarkerTransitionApplyRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasCurrentMarker)
            return request.CurrentMarker;

        return await markerStore.GetMarkerAsync(request.ResourceId, request.TenantScope, cancellationToken);
    }

    private static ResourceLifecycleMarkerTransitionResult Failure(
        ResourceLifecycleMarkerTransitionStatus status,
        string code,
        string message,
        string resourceId) =>
        new()
        {
            Status = status,
            Diagnostics =
            [
                ResourcePolicyValidator.Diagnostic(
                    code,
                    message,
                    resourceId: resourceId),
            ],
        };
}

internal sealed class ResourceLifecycleMarkerTransitionApplyRequest
{
    public required TenantScope TenantScope { get; init; }

    public required string ResourceId { get; init; }

    public required ResourceLifecycleMarkerState State { get; init; }

    public required DateTimeOffset MarkedAt { get; init; }

    public string? Reason { get; init; }

    public ResourceLifecycleMarker? CurrentMarker { get; init; }

    public bool HasCurrentMarker { get; init; }
}

internal sealed record ResourceLifecycleMarkerTransitionResult
{
    public required ResourceLifecycleMarkerTransitionStatus Status { get; init; }

    public ResourceLifecycleMarker? Marker { get; init; }

    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}

internal enum ResourceLifecycleMarkerTransitionStatus
{
    Applied,
    AlreadySatisfied,
    Failed,
}
