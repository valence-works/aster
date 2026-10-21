using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

/// <summary>
/// Writes and clears lifecycle markers for the marker, restore, and policy application services, and runs the archive
/// and restore lifecycle hooks around each write (#116).
/// </summary>
/// <remarks>
/// Hooks run only when a marker is actually written or cleared. A before-hook rejection or failure, and an after-hook
/// failure, become a diagnostic on the result, as the portability service does, because these services report
/// per-resource results rather than throw. A failed store write still runs the after hooks with a
/// <see cref="LifecycleOperationStatus.Failed"/> status, then rethrows the store's exception. So does a marker write that
/// loses a race with another writer of the same resource, which then returns the result of the marker it lost to.
/// </remarks>
internal sealed partial class ResourceLifecycleMarkerTransitionService : IResourceLifecycleMarkerTransitionService
{
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;
    private readonly IResourceLifecycleMarkerClearStore? markerClearStore;
    private readonly IResourceLifecycleHookDispatcher lifecycleHooks;
    private readonly ILogger logger;

    public ResourceLifecycleMarkerTransitionService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore)
        : this(versionReader, markerStore, NoopResourceLifecycleHookDispatcher.Instance, NullLogger<ResourceLifecycleMarkerTransitionService>.Instance)
    {
    }

    public ResourceLifecycleMarkerTransitionService(
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore,
        IResourceLifecycleHookDispatcher lifecycleHooks,
        ILogger<ResourceLifecycleMarkerTransitionService> logger)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        ArgumentNullException.ThrowIfNull(lifecycleHooks);
        ArgumentNullException.ThrowIfNull(logger);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.lifecycleHooks = lifecycleHooks;
        this.logger = logger;
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

        var target = await ResolveTargetAsync(request, cancellationToken);
        if (!target.Exists)
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
            return ExistingMarker(request, existing);

        var proposed = new ResourceLifecycleMarker
        {
            TenantScope = request.TenantScope,
            ResourceId = request.ResourceId,
            State = request.State,
            MarkedAt = request.MarkedAt,
            Reason = request.Reason,
        };
        var context = new ResourceArchiveLifecycleContext
        {
            OperationId = Guid.NewGuid(),
            LifecyclePoint = LifecyclePoint.BeforeArchive,
            CancellationToken = cancellationToken,
            TenantScope = request.TenantScope,
            ResourceId = request.ResourceId,
            DefinitionId = target.DefinitionId,
            Marker = proposed,
        };

        try
        {
            await lifecycleHooks.InvokeBeforeArchiveAsync(context, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return HookFailure(exception, request.ResourceId);
        }

        context = context with { LifecyclePoint = LifecyclePoint.AfterArchive };
        ResourceLifecycleMarkerCreateResult created;
        try
        {
            created = await markerStore.CreateMarkerAsync(proposed, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterFailedWriteAsync(context, LifecycleOperationFailures.FromException(exception), cancellationToken);
            throw;
        }

        if (!created.Created)
        {
            // Another writer marked the resource between the read and the write (#238): this call wrote nothing, and
            // gets the result the read would have given it had it run after the other write.
            await TryInvokeAfterFailedWriteAsync(
                context,
                new LifecycleOperationFailure
                {
                    Code = LifecycleOperationFailure.OperationFailedCode,
                    Message = $"Resource '{request.ResourceId}' was marked as {created.Marker.State} before its {request.State} marker could be written.",
                },
                cancellationToken);
            return ExistingMarker(request, created.Marker);
        }

        var marker = created.Marker;
        var result = new ResourceLifecycleMarkerTransitionResult
        {
            Status = ResourceLifecycleMarkerTransitionStatus.Applied,
            Marker = marker,
        };

        try
        {
            await lifecycleHooks.InvokeAfterArchiveAsync(
                context with { OperationStatus = LifecycleOperationStatus.Succeeded, Marker = marker },
                cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            result = result with { Diagnostics = [HookDiagnostic(exception, request.ResourceId)] };
        }

        return result;
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

        var target = await ResolveTargetAsync(request, cancellationToken);
        if (!target.Exists)
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

        var context = new ResourceArchiveLifecycleContext
        {
            OperationId = Guid.NewGuid(),
            LifecyclePoint = LifecyclePoint.BeforeRestore,
            CancellationToken = cancellationToken,
            TenantScope = request.TenantScope,
            ResourceId = request.ResourceId,
            DefinitionId = target.DefinitionId,
            Marker = existing,
        };

        try
        {
            await lifecycleHooks.InvokeBeforeRestoreAsync(context, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return HookFailure(exception, request.ResourceId, existing);
        }

        context = context with { LifecyclePoint = LifecyclePoint.AfterRestore };
        bool removed;
        try
        {
            removed = await clearStore.ClearMarkerAsync(
                request.ResourceId,
                request.TenantScope,
                request.ExpectedState,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterFailedWriteAsync(context, LifecycleOperationFailures.FromException(exception), cancellationToken);
            throw;
        }

        if (removed)
        {
            var result = new ResourceLifecycleMarkerTransitionResult
            {
                Status = ResourceLifecycleMarkerTransitionStatus.Cleared,
                Marker = existing,
            };

            try
            {
                await lifecycleHooks.InvokeAfterRestoreAsync(
                    context with { OperationStatus = LifecycleOperationStatus.Succeeded },
                    cancellationToken);
            }
            catch (LifecycleHookException exception)
            {
                result = result with { Diagnostics = [HookDiagnostic(exception, request.ResourceId)] };
            }

            return result;
        }

        // Another writer changed or cleared the marker between the read and the clear: this call cleared nothing.
        await TryInvokeAfterFailedWriteAsync(
            context,
            new LifecycleOperationFailure
            {
                Code = LifecycleOperationFailure.OperationFailedCode,
                Message = $"The {request.ExpectedState} marker of resource '{request.ResourceId}' changed before it could be cleared.",
            },
            cancellationToken);

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

    private async ValueTask<(bool Exists, string? DefinitionId)> ResolveTargetAsync(
        ResourceLifecycleMarkerTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasTargetExistence)
            return (request.TargetExists, request.TargetDefinitionId);

        var resources = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = request.TenantScope,
            Scope = ResourceVersionScope.Latest,
            ResourceIds = [request.ResourceId],
        }, cancellationToken);

        var latest = resources.FirstOrDefault(resource => string.Equals(resource.ResourceId, request.ResourceId, StringComparison.Ordinal));
        return (latest is not null, latest?.DefinitionId);
    }

    private async ValueTask<ResourceLifecycleMarker?> ResolveCurrentMarkerAsync(
        ResourceLifecycleMarkerTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HasCurrentMarker)
            return request.CurrentMarker;

        return await markerStore.GetMarkerAsync(request.ResourceId, request.TenantScope, cancellationToken);
    }

    // A failed write still runs the after hooks so before/after calls stay paired, with a Failed outcome so no hook
    // mistakes it for a completed change (#219). A hook failure here is logged rather than reported: the write's own
    // failure is what the caller receives.
    private async ValueTask TryInvokeAfterFailedWriteAsync(
        ResourceArchiveLifecycleContext context,
        LifecycleOperationFailure failure,
        CancellationToken cancellationToken)
    {
        var failed = context with
        {
            OperationStatus = LifecycleOperationStatus.Failed,
            OperationFailure = failure,
        };

        try
        {
            if (context.LifecyclePoint == LifecyclePoint.AfterArchive)
                await lifecycleHooks.InvokeAfterArchiveAsync(failed, cancellationToken);
            else
                await lifecycleHooks.InvokeAfterRestoreAsync(failed, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAfterHookFailedOnFailedWrite(exception, context.LifecyclePoint, context.ResourceId);
        }
    }

    private static ResourceLifecycleMarkerTransitionResult HookFailure(
        LifecycleHookException exception,
        string resourceId,
        ResourceLifecycleMarker? marker = null) =>
        new()
        {
            Status = ResourceLifecycleMarkerTransitionStatus.Failed,
            Marker = marker,
            Diagnostics = [HookDiagnostic(exception, resourceId)],
        };

    private static ResourcePolicyDiagnostic HookDiagnostic(LifecycleHookException exception, string resourceId) =>
        ResourcePolicyValidator.Diagnostic(
            exception.Code,
            exception.Message,
            $"lifecycle/{LifecyclePointPath(exception.LifecyclePoint)}",
            resourceId: resourceId);

    private static string LifecyclePointPath(LifecyclePoint lifecyclePoint)
    {
        var name = lifecyclePoint.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static ResourceLifecycleMarkerTransitionResult ExistingMarker(
        ResourceLifecycleMarkerTransitionApplyRequest request,
        ResourceLifecycleMarker existing)
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

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "{LifecyclePoint} hook failed for resource '{ResourceId}' after the marker write itself failed; the write's failure is what the caller receives.")]
    private partial void LogAfterHookFailedOnFailedWrite(Exception exception, LifecyclePoint lifecyclePoint, string resourceId);
}

internal abstract class ResourceLifecycleMarkerTransitionRequest
{
    public required TenantScope TenantScope { get; init; }

    public required string ResourceId { get; init; }

    public bool TargetExists { get; init; }

    public bool HasTargetExistence { get; init; }

    /// <summary>
    /// Definition of the target's latest version, passed with <see cref="TargetExists"/> so the archive and restore
    /// hooks get it without another read.
    /// </summary>
    public string? TargetDefinitionId { get; init; }

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
