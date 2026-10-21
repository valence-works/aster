# Contract: Host Lifecycle Hooks

## Public SDK Behavior

The SDK exposes explicit host lifecycle hooks for:

1. Resource save operations.
2. Activation and deactivation operations.
3. Snapshot export.
4. Import preview.
5. Write import.
6. Archive and restore: writing and clearing a resource's archive or soft-delete lifecycle marker (#116).

The workflow MUST NOT introduce recipes, workflow engines, live synchronization, runtime scanning, background job orchestration, provider registries, public SQL, or public `IQueryable<Resource>`.

## Registration Contract

Hosts register hooks explicitly through normal service registration.

Rules:

- Registration order defines execution order.
- No hook is discovered automatically.
- No hook registration is required for existing behavior.
- Providers do not need provider-specific hook registration.

Proposed shape:

```csharp
services
    .AddAsterCore()
    .AddResourceLifecycleHook<MyAuditHook>()
    .AddResourceLifecycleHook<MyPolicyHook>();
```

Manual service registration remains supported for advanced hosts if it produces the same hook contract registrations.

## Hook Contract

Proposed public contract:

```csharp
public interface IResourceLifecycleHook
{
    ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LifecycleHookOutcome> OnBeforeActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnAfterActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LifecycleHookOutcome> OnBeforeDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnAfterDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LifecycleHookOutcome> OnBeforeExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnAfterExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LifecycleHookOutcome> OnBeforePreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnAfterPreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask<LifecycleHookOutcome> OnBeforeImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnAfterImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);
}
```

Implementation may split this into smaller focused interfaces during planning/implementation if that better preserves simplicity and avoids forcing hooks to implement unrelated methods. The public behavior remains the same: explicit hooks, deterministic order, before rejection, and after-success observation.

### Archive and restore points (#116)

Added after the original contract as default interface members, so existing hooks and dispatchers keep compiling:

```csharp
public interface IResourceLifecycleHook
{
    // ...
    ValueTask<LifecycleHookOutcome> OnBeforeArchiveAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(LifecycleHookOutcome.Continue());
    ValueTask OnAfterArchiveAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
    ValueTask<LifecycleHookOutcome> OnBeforeRestoreAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(LifecycleHookOutcome.Continue());
    ValueTask OnAfterRestoreAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

public sealed record ResourceArchiveLifecycleContext : ResourceLifecycleContext
{
    public required string ResourceId { get; init; }
    public string? DefinitionId { get; init; }
    public required ResourceLifecycleMarker Marker { get; init; }
}
```

`ResourceLifecycleHook` gains matching virtual members and `IResourceLifecycleHookDispatcher` gains `InvokeBefore/AfterArchiveAsync` and `InvokeBefore/AfterRestoreAsync` (default: no hooks run).

Rules:

- The archive points run around every lifecycle marker write: `IResourceLifecycleMarkerService.ApplyAsync` and policy application (`IResourcePolicyApplicationService.ApplyAsync`). They cover both marker states; `Marker.State` says which.
- The restore points run around every marker clear by `IResourceLifecycleRestoreService.RestoreAsync`.
- Hooks run only when the call writes. A marker already in the requested state, a marker conflict, a missing resource, a restore preview, and an already-restored resource run no hooks.
- Before and after calls are paired and carry the outcome like the other points. A store write that throws runs the after hooks with `Failed` (`lifecycle-operation-failed`) and is rethrown. A restore whose clear removes nothing, because another writer changed the marker first, also runs them with `Failed`.

## Outcome Contract

```csharp
public sealed record LifecycleHookOutcome
{
    public required LifecycleHookOutcomeStatus Status { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
}

public enum LifecycleHookOutcomeStatus
{
    Continue,
    Rejected,
    Failed,
}
```

Rules:

- `Continue` allows invocation to continue.
- `Rejected` is valid for before hooks and blocks the underlying operation.
- `Failed` represents a hook failure that must be visible to the caller.
- Cancellation uses normal cancellation semantics.

## Context Contract

Context records are immutable and lifecycle-specific.

Required context families:

- Save lifecycle context.
- Activation lifecycle context.
- Export lifecycle context.
- Import lifecycle context.

Rules:

- Context MUST expose operation identity and lifecycle point.
- Save context MUST distinguish create, update, and schema upgrade.
- Activation context MUST expose resource ID, version, channel, and multiple-active choice.
- Export context MUST expose export request and after-export result when available.
- Import context MUST expose snapshot, import options, preview/import result when available.
- Context MUST NOT expose provider-specific storage implementation details.

## Failure Contract

Rules:

- Save, activation, and deactivation hook rejections/failures surface as structured lifecycle hook exceptions.
- Export, preview import, and write import hook rejections/failures surface as portability diagnostics on the existing result objects where possible.
- Archive and restore hook rejections/failures surface as policy diagnostics on the marker, restore, or policy application result, with the hook's code and the path `lifecycle/<point>` (#116). A before-hook rejection fails that resource and writes nothing; an after-hook failure leaves the marker written.
- After-hook failures are visible but do not imply rollback of already-completed state.
- No later hooks run after rejection, failure, or cancellation.

Stable failure/diagnostic codes should include:

- `lifecycle-hook-rejected`
- `lifecycle-hook-failed`

Cancellation uses normal cancellation semantics and is not converted into a lifecycle failure code.

## Operation Outcome Contract

Added by #219. See the spec's [Failure Semantics](../spec.md#failure-semantics).

```csharp
public abstract record ResourceLifecycleContext
{
    // ...
    public LifecycleOperationStatus OperationStatus { get; init; }   // Pending | Succeeded | Failed
    public LifecycleOperationFailure? OperationFailure { get; init; } // set when Failed
}

public sealed record LifecycleOperationFailure
{
    public const string OperationFailedCode = "lifecycle-operation-failed";
    public required string Code { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
}
```

Rules:

- Before hooks see `Pending`.
- After hooks run once all before hooks continued, whether the operation succeeded or failed. They see `Succeeded` or `Failed`.
- `Failed` means the state change the context describes did not happen. On a failed activation or deactivation, `ActiveVersions` is the set the write tried to store. On a failed save, `Resource` is the proposed version.
- Save, activation, and deactivation failures use `lifecycle-operation-failed` with the thrown exception. Portability failures use the code of the error diagnostic that failed the operation.

## Compatibility Contract

Rules:

- With no hooks registered, existing create, update, schema upgrade, activation, deactivation, export, preview import, and write import behavior MUST remain unchanged.
- Hook registration MUST NOT require provider changes.
- Hook support MUST NOT change query behavior or provider capability validation.
