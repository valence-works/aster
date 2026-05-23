# Contract: Host Lifecycle Hooks

## Public SDK Behavior

The SDK exposes explicit host lifecycle hooks for:

1. Resource save operations.
2. Activation and deactivation operations.
3. Snapshot export.
4. Import preview.
5. Write import.

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
- After-hook failures are visible but do not imply rollback of already-completed state.
- No later hooks run after rejection, failure, or cancellation.

Stable failure/diagnostic codes should include:

- `lifecycle-hook-rejected`
- `lifecycle-hook-failed`

Cancellation uses normal cancellation semantics and is not converted into a lifecycle failure code.

## Compatibility Contract

Rules:

- With no hooks registered, existing create, update, schema upgrade, activation, deactivation, export, preview import, and write import behavior MUST remain unchanged.
- Hook registration MUST NOT require provider changes.
- Hook support MUST NOT change query behavior or provider capability validation.
