# Contract: Lifecycle Restore Summaries

## Public SDK Behavior

The core SDK exposes pure summary helpers for lifecycle restore result objects.

### Application Summary

`ResourceLifecycleRestoreApplicationResult.ToSummary()` returns a `ResourceLifecycleRestoreApplicationSummary`.

Required behavior:

- Throws `ArgumentNullException` when the source result is null.
- Preserves `TenantScope`.
- Preserves `RestoredAt`.
- Counts all candidate statuses.
- Sets `HasFailures` when one or more candidates failed.
- Sets `IsFullySuccessful` only when every candidate is `Restored` or `AlreadyRestored`.
- Counts distinct nonblank resource identifiers from `Restored` and `AlreadyRestored` candidates.
- Aggregates nonblank diagnostic codes in ordinal code order.
- Treats null candidate and diagnostic collections as empty.

### Preview Summary

`ResourceLifecycleRestorePreviewResult.ToSummary()` returns a `ResourceLifecycleRestorePreviewSummary`.

Required behavior:

- Throws `ArgumentNullException` when the source result is null.
- Preserves `TenantScope`.
- Counts all candidate statuses.
- Sets `HasFailures` when one or more candidates failed.
- Sets `IsFullySuccessful` only when every candidate is `Restorable` or `AlreadyRestored`.
- Counts distinct nonblank resource identifiers from `Restorable` and `AlreadyRestored` candidates.
- Aggregates nonblank diagnostic codes in ordinal code order.
- Treats null candidate and diagnostic collections as empty.

## Non-Goals

This contract does not add:

- New restore preview or application behavior.
- Service registration.
- Storage or provider behavior.
- Audit persistence.
- Reporting infrastructure.
- Query planning.
- Public SQL.
- Public `IQueryable<Resource>`.
- Runtime scanning or automatic discovery.
- Background jobs or schedulers.
