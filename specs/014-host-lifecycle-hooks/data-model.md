# Data Model: Host Lifecycle Hooks

## Lifecycle Hook Registration

Host-provided service registration that opts into one or more lifecycle points.

**Fields**:

- `HookType`: concrete host hook service type.
- `LifecyclePoints`: lifecycle points the hook handles.
- `RegistrationOrder`: deterministic order derived from service registration order.

**Validation rules**:

- Hook registration MUST be explicit.
- Hooks MUST NOT be discovered through runtime scanning, naming conventions, or attributes.
- Multiple hooks for the same lifecycle point run in registration order.
- Duplicate hook registrations are allowed and execute as separate registrations unless a later implementation explicitly documents otherwise.

## Lifecycle Point

Named moment in a core operation where hooks may run.

**Values**:

- `BeforeSave`
- `AfterSave`
- `BeforeActivate`
- `AfterActivate`
- `BeforeDeactivate`
- `AfterDeactivate`
- `BeforeExport`
- `AfterExport`
- `BeforePreviewImport`
- `AfterPreviewImport`
- `BeforeImport`
- `AfterImport`

**Validation rules**:

- Before lifecycle points run before the underlying operation mutates state or materializes a completed export.
- After lifecycle points run only after the underlying operation succeeds.
- No after-success hook runs when the underlying operation fails.

## Lifecycle Hook Context

Immutable operation-specific data supplied to a hook.

**Shared fields**:

- `LifecyclePoint`: hook point being invoked.
- `OperationId`: per-operation identifier useful for correlating before/after observations.
- `CancellationToken`: cancellation signal for the operation.

**Save context fields**:

- `SaveKind`: create, update, or schema upgrade.
- `DefinitionId`: logical definition identifier.
- `ResourceId`: logical resource identifier when known.
- `BaseVersion`: optimistic concurrency base version when applicable.
- `Resource`: saved resource version for after-save hooks.

**Activation context fields**:

- `ResourceId`: logical resource identifier.
- `Version`: resource version.
- `Channel`: activation channel.
- `AllowMultipleActive`: whether activation allows multiple active versions.
- `ActiveVersions`: resulting active versions for after hooks when available.

**Portability context fields**:

- `ExportRequest`: export request for export hooks.
- `Snapshot`: portable snapshot for preview/import hooks or after-export hooks when available.
- `ImportOptions`: import options for preview/import hooks.
- `Preview`: preview result for after-preview hooks.
- `ImportResult`: import result for after-import hooks.
- `ExportResult`: export result for after-export hooks.

**Validation rules**:

- Context records MUST be immutable or otherwise protect operation state from hook mutation.
- Context MUST include enough identity and option data for auditing and policy decisions.
- Context MUST NOT expose provider-specific storage implementation details.

## Lifecycle Hook Outcome

Result returned by hook invocation.

**Fields**:

- `Status`: continue, rejected, or failed.
- `Code`: stable machine-readable code when rejected or failed.
- `Message`: human-readable explanation.
- `Diagnostics`: optional structured diagnostics for result-based operations.

**Validation rules**:

- A continue outcome lets invocation proceed to the next hook or underlying operation.
- A rejected outcome from a before hook stops later hooks and prevents the underlying operation.
- A failed outcome from a thrown hook or explicit failure stops later hooks and is surfaced to the caller.
- After-hook failures MUST be visible but MUST NOT claim rollback of already-completed state.

## Lifecycle Hook Failure

Structured failure presented to the caller when hook invocation blocks or fails an operation.

**Fields**:

- `Code`: stable failure code such as `lifecycle-hook-rejected` or `lifecycle-hook-failed`.
- `LifecyclePoint`: hook point where the failure occurred.
- `HookType`: hook type or display name when available.
- `Message`: human-readable failure detail.
- `Diagnostics`: operation-specific diagnostics when supported.

**Validation rules**:

- Save, activation, and deactivation hook failures SHOULD surface through structured exceptions because those operations currently fail with exceptions.
- Export, preview import, and write import hook failures SHOULD surface as portability diagnostics because those operations already return diagnostics.
- Cancellation MUST preserve normal cancellation semantics through `OperationCanceledException`.

## State Transitions

```text
Resource save request
  -> before-save hooks
  -> reject/fail/cancel OR persist new resource version
  -> after-save hooks
  -> return saved resource OR visible hook failure

Activation/deactivation request
  -> before activation/deactivation hooks
  -> reject/fail/cancel OR update activation state
  -> after activation/deactivation hooks
  -> complete OR visible hook failure

Export request
  -> before-export hooks
  -> reject/fail/cancel OR build export result
  -> after-export hooks when export succeeds
  -> return export result with diagnostics as needed

Import preview request
  -> before-preview hooks
  -> reject/fail/cancel OR build preview
  -> after-preview hooks when preview completes
  -> return preview with diagnostics as needed

Write import request
  -> before-import hooks
  -> reject/fail/cancel OR plan/apply import
  -> after-import hooks when import succeeds
  -> return import result with diagnostics as needed
```
