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

    ValueTask<ResourceLifecycleMarkerTransitionResult> ClearAsync(
        ResourceLifecycleMarkerTransitionClearRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ResourceLifecycleMarkerTransitionService : IResourceLifecycleMarkerTransitionService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;
    private readonly IResourceLifecycleMarkerClearStore? markerClearStore;

    public ResourceLifecycleMarkerTransitionService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        markerClearStore = markerStore as IResourceLifecycleMarkerClearStore;
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
                request.ResourceId,
                request.TargetNotFoundDiagnosticPath);
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

    public async ValueTask<ResourceLifecycleMarkerTransitionResult> ClearAsync(
        ResourceLifecycleMarkerTransitionClearRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceId);

        if (request.ExpectedState == ResourceLifecycleMarkerState.None || !Enum.IsDefined(request.ExpectedState))
        {
            return Failure(
                ResourceLifecycleMarkerTransitionStatus.Failed,
                ResourcePolicyDiagnosticCodes.LifecycleRestoreStateUnsupported,
                $"Lifecycle restore expected state '{request.ExpectedState}' is not supported.",
                request.ResourceId);
        }

        if (!await TargetExistsAsync(request, cancellationToken))
        {
            return Failure(
                ResourceLifecycleMarkerTransitionStatus.Failed,
                ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound,
                $"Resource '{request.ResourceId}' was not found in tenant '{request.TenantScope.TenantId}'.",
                request.ResourceId,
                request.TargetNotFoundDiagnosticPath);
        }

        var existing = await ResolveCurrentMarkerAsync(request, cancellationToken);
        if (existing is null)
        {
            return new ResourceLifecycleMarkerTransitionResult
            {
                Status = ResourceLifecycleMarkerTransitionStatus.AlreadyCleared,
            };
        }

        if (existing.State != request.ExpectedState)
            return MarkerMismatch(request, existing);

        if (!request.Apply)
        {
            return new ResourceLifecycleMarkerTransitionResult
            {
                Status = ResourceLifecycleMarkerTransitionStatus.ReadyToClear,
                Marker = existing,
            };
        }

        var clearStore = markerClearStore
            ?? throw new InvalidOperationException(
                $"The active {nameof(IResourceLifecycleMarkerStore)} registration must also implement {nameof(IResourceLifecycleMarkerClearStore)} to clear lifecycle markers.");
        var removed = await clearStore.ClearMarkerAsync(
            request.ResourceId,
            request.TenantScope,
            request.ExpectedState,
            cancellationToken);
        if (removed)
        {
            return new ResourceLifecycleMarkerTransitionResult
            {
                Status = ResourceLifecycleMarkerTransitionStatus.Cleared,
                Marker = existing,
            };
        }

        var current = await markerStore.GetMarkerAsync(request.ResourceId, request.TenantScope, cancellationToken);
        if (current is null)
        {
            return new ResourceLifecycleMarkerTransitionResult
            {
                Status = ResourceLifecycleMarkerTransitionStatus.AlreadyCleared,
            };
        }

        return MarkerMismatch(request, current);
    }

    private async ValueTask<bool> TargetExistsAsync(
        ResourceLifecycleMarkerTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasTargetExistence)
            return request.TargetExists;

        var resources = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = request.TenantScope,
            Scope = ResourceVersionScope.Latest,
            ResourceIds = [request.ResourceId],
        }, cancellationToken);

        return resources.Any(resource => string.Equals(resource.ResourceId, request.ResourceId, StringComparison.Ordinal));
    }

    private async ValueTask<ResourceLifecycleMarker?> ResolveCurrentMarkerAsync(
        ResourceLifecycleMarkerTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasCurrentMarker)
            return request.CurrentMarker;

        return await markerStore.GetMarkerAsync(request.ResourceId, request.TenantScope, cancellationToken);
    }

    private static ResourceLifecycleMarkerTransitionResult MarkerMismatch(
        ResourceLifecycleMarkerTransitionClearRequest request,
        ResourceLifecycleMarker marker) =>
        new()
        {
            Status = ResourceLifecycleMarkerTransitionStatus.MarkerMismatch,
            Marker = marker,
            Diagnostics =
            [
                ResourcePolicyValidator.Diagnostic(
                    ResourcePolicyDiagnosticCodes.LifecycleRestoreMarkerMismatch,
                    $"Resource '{request.ResourceId}' is marked as {marker.State}; expected {request.ExpectedState}.",
                    "expectedState",
                    resourceId: request.ResourceId),
            ],
        };

    private static ResourceLifecycleMarkerTransitionResult Failure(
        ResourceLifecycleMarkerTransitionStatus status,
        string code,
        string message,
        string resourceId,
        string? path = null) =>
        new()
        {
            Status = status,
            Diagnostics =
            [
                ResourcePolicyValidator.Diagnostic(
                    code,
                    message,
                    path,
                    resourceId: resourceId),
            ],
        };
}

internal abstract class ResourceLifecycleMarkerTransitionRequest
{
    public required TenantScope TenantScope { get; init; }

    public required string ResourceId { get; init; }

    public bool TargetExists { get; init; }

    public bool HasTargetExistence { get; init; }

    public ResourceLifecycleMarker? CurrentMarker { get; init; }

    public bool HasCurrentMarker { get; init; }

    public string? TargetNotFoundDiagnosticPath { get; init; }
}

internal sealed class ResourceLifecycleMarkerTransitionApplyRequest : ResourceLifecycleMarkerTransitionRequest
{
    public required ResourceLifecycleMarkerState State { get; init; }

    public required DateTimeOffset MarkedAt { get; init; }

    public string? Reason { get; init; }
}

internal sealed class ResourceLifecycleMarkerTransitionClearRequest : ResourceLifecycleMarkerTransitionRequest
{
    public required ResourceLifecycleMarkerState ExpectedState { get; init; }

    public bool Apply { get; init; }
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
    ReadyToClear,
    Cleared,
    AlreadyCleared,
    MarkerMismatch,
    Failed,
}
