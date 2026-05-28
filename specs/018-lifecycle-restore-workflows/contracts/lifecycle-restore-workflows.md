# Contract: Lifecycle Restore Workflows

## Public SDK Behavior

Lifecycle restore workflows provide host-controlled, tenant-scoped preview and application for archive and soft-delete marker restoration.

The SDK MUST:

- let hosts preview selected restore candidates without writes;
- let hosts apply selected restore candidates explicitly;
- clear only existing archive or soft-delete lifecycle marker state;
- return exactly one ordered result per input candidate;
- preserve resource versions, activation state, resource definitions, policy declarations, and portability snapshot format;
- keep omitted tenant scope mapped to the default single-tenant scope;
- fail closed when current marker state differs from the candidate's expected state.

The SDK MUST NOT:

- restore automatically from policy declarations or preview results;
- clear markers outside the effective tenant;
- rewrite versions, deactivate active versions, delete resources, or perform pruning writes;
- add public SQL, public `IQueryable<Resource>`, runtime scanning, provider registries, schedulers, hidden jobs, authorization engines, or a general lifecycle state machine.

## Proposed Host-Facing Contract

The restore workflow uses a focused host-facing service.

```csharp
public interface IResourceLifecycleRestoreService
{
    ValueTask<ResourceLifecycleRestorePreviewResult> PreviewRestoreAsync(
        ResourceLifecycleRestoreRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ResourceLifecycleRestoreApplicationResult> RestoreAsync(
        ResourceLifecycleRestoreRequest request,
        CancellationToken cancellationToken = default);
}
```

Contract rules:

- `PreviewRestoreAsync` MUST be non-mutating.
- `RestoreAsync` MUST re-read current target and marker state; it MUST NOT trust an earlier preview result.
- Both methods MUST resolve one effective tenant per request.
- Both methods MUST batch target resource and marker reads by distinct candidate resource IDs where possible.
- Both methods MUST return empty results for empty candidate lists.
- Both methods MUST produce one result per input candidate in input order.

## Proposed Provider-Facing Contract

The lifecycle marker storage layer adds a narrow clear capability.

```csharp
public interface IResourceLifecycleMarkerClearStore : IResourceLifecycleMarkerStore
{
    ValueTask<bool> ClearMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
```

Contract rules:

- `ClearMarkerAsync` MUST remove the effective lifecycle marker for the resource in the supplied tenant.
- `ClearMarkerAsync` MUST return `true` when a marker existed and was removed.
- `ClearMarkerAsync` MUST return `false` when no marker existed.
- `ClearMarkerAsync` MUST NOT remove resource versions, activation state, definitions, policy declarations, or markers in other tenants.
- Providers SHOULD implement clear through the existing lifecycle marker storage mechanism without schema changes.
- Providers that do not implement the clear capability do not support lifecycle restore workflows.

## Restore Candidate Semantics

Required candidate fields:

- `ResourceId`
- `ExpectedState`

Supported expected states:

- `Archived`
- `SoftDeleted`

Unsupported expected states:

- `None`
- unknown enum values
- pruning outcomes or other policy outcomes
- activation or retained concepts

## Result Semantics

Preview result statuses:

- `Restorable`: current marker matches expected state.
- `AlreadyRestored`: target exists and no marker is present.
- `Skipped`: no write is required because a prior duplicate outcome already determined the result.
- `Failed`: invalid candidate, unsupported state, missing target, or marker mismatch.

Application result statuses:

- `Restored`: marker matched expected state and was cleared.
- `AlreadyRestored`: target exists and no marker is present, including retry after prior success.
- `Skipped`: no write is required because a prior duplicate outcome already determined the result.
- `Failed`: invalid candidate, unsupported state, missing target, or marker mismatch.

## Stable Diagnostics

The implementation MUST expose stable diagnostic codes for:

- invalid restore candidate shape;
- unsupported restore state;
- missing target resource;
- marker-state mismatch.

Existing `lifecycle-marker-target-not-found` MAY be reused for missing target resources to preserve current lifecycle diagnostic semantics.

## Compatibility

Existing behavior MUST remain unchanged for:

- direct lifecycle marker apply;
- policy preview;
- policy application;
- resource write/update;
- activation/deactivation;
- lifecycle-state queries except that restored resources no longer match archived/soft-deleted filters after marker clearing;
- portability export/import of marker state;
- lifecycle hooks.
