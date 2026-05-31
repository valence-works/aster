# Contract: Policy Preview Summaries

## Public SDK Behavior

The core SDK exposes a pure summary helper for policy preview result objects.

`ResourcePolicyEvaluationPreview.ToSummary()` returns a `ResourcePolicyPreviewSummary`.

Required behavior:

- Throws `ArgumentNullException` when the source preview is null.
- Preserves `TenantScope`.
- Preserves `EvaluationTimestamp`.
- Counts all preview candidates.
- Counts distinct nonblank resource identifiers across candidates.
- Counts distinct `(resourceId, resourceVersion)` targets where both values are present.
- Aggregates candidates by `ResourcePolicyOutcome` in deterministic enum order.
- Aggregates candidates by `ResourcePolicyKind` in deterministic enum order.
- Aggregates nonblank diagnostic codes in ordinal code order.
- Treats null candidate and diagnostic collections as empty.
- Performs no service resolution, provider access, storage access, policy evaluation, validation, or mutation.

## Non-Goals

This contract does not add:

- New policy preview behavior.
- New policy application behavior.
- Service registration.
- Storage or provider behavior.
- Audit persistence.
- Reporting infrastructure.
- Query planning.
- Public SQL.
- Public `IQueryable<Resource>`.
- Runtime scanning or automatic discovery.
- Background jobs or schedulers.
