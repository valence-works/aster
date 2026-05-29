# Data Model: Lifecycle Restore Workflows

## Lifecycle Restore Request

Host-provided request for previewing or applying restore candidates.

Fields:

- `TenantScope`: optional tenant scope; omitted means the default single-tenant scope.
- `Candidates`: ordered list of lifecycle restore candidates.
- `RestoredAt`: optional host timestamp for application reporting if implementation chooses to expose it; it does not create resource history.

Validation rules:

- A null request is invalid.
- Empty candidates are valid and return an empty result.
- The effective tenant is resolved once per request.
- The request must not imply cross-tenant restore.

## Lifecycle Restore Candidate

Host-selected restore target.

Fields:

- `ResourceId`: logical resource identifier.
- `ExpectedState`: lifecycle marker state expected to be cleared.

Validation rules:

- `ResourceId` is required and must not be empty or whitespace.
- `ExpectedState` must be `Archived` or `SoftDeleted`.
- `None`, unknown enum values, active/retained concepts, pruning outcomes, and arbitrary states are unsupported.

## Lifecycle Restore Preview Result

Non-mutating response for a restore preview request.

Fields:

- `TenantScope`: effective tenant used for the preview.
- `Candidates`: ordered preview candidate results, one per input candidate.
- Aggregate counts for restorable, already restored, skipped, and failed candidates.

Validation rules:

- Preview must not clear marker state.
- Preview must read current target resources and marker state in the effective tenant only.
- Preview must report missing target resources separately from already-restored resources.

## Lifecycle Restore Application Result

Write response for a restore application request.

Fields:

- `TenantScope`: effective tenant used for application.
- `Candidates`: ordered application candidate results, one per input candidate.
- Aggregate counts for restored, already restored, skipped, and failed candidates.

Validation rules:

- Application revalidates current resource existence and marker state.
- Application clears marker state only when the current marker matches the expected state.
- Application allows partial success for unrelated candidates.
- Application updates its in-memory marker view after clearing a marker so duplicates are deterministic.

## Restore Candidate Result

Per-candidate outcome for preview or application.

Fields:

- `Index`: zero-based input index.
- `ResourceId`: input resource identifier when available.
- `ExpectedState`: expected marker state when valid.
- `Status`: candidate status.
- `Diagnostics`: stable diagnostics for failed candidates.

Preview statuses:

- `Restorable`: target exists and current marker matches expected state.
- `AlreadyRestored`: target exists and no marker is present.
- `Skipped`: deterministic duplicate after a prior result where no additional outcome is needed.
- `Failed`: invalid shape, missing target, unsupported state, or marker mismatch.

Application statuses:

- `Restored`: target existed, marker matched expected state, and marker state was cleared.
- `AlreadyRestored`: target exists and no matching marker remains.
- `Skipped`: deterministic duplicate after a prior result where no write is needed.
- `Failed`: invalid shape, missing target, unsupported state, or marker mismatch.

## Restore Diagnostic

Stable per-candidate diagnostic.

Diagnostic needs:

- `lifecycle-restore-candidate-invalid`: missing resource identity, missing expected state, or malformed candidate.
- `lifecycle-restore-state-unsupported`: expected state is not archive or soft-delete.
- `lifecycle-marker-target-not-found`: resource does not exist in the effective tenant.
- `lifecycle-restore-marker-mismatch`: current marker state differs from expected state.

Diagnostic rules:

- Diagnostics must be stable enough for tests and host UI branching.
- Tenant-scoped target misses use the same not-found behavior as existing lifecycle marker writes.
- Marker mismatch must include the resource identity and a path that identifies expected/current state when available.

## State Transitions

```text
No marker
  -- preview restore archive/soft-delete --> AlreadyRestored
  -- apply restore archive/soft-delete --> AlreadyRestored

Archived marker
  -- preview restore Archived --> Restorable
  -- apply restore Archived --> No marker
  -- preview/apply restore SoftDeleted --> Failed(marker mismatch)

SoftDeleted marker
  -- preview restore SoftDeleted --> Restorable
  -- apply restore SoftDeleted --> No marker
  -- preview/apply restore Archived --> Failed(marker mismatch)
```

Prohibited transitions:

- Restore rewriting resource versions.
- Restore changing activation state.
- Restore clearing markers in another tenant.
- Restore clearing a different state than the candidate expected.
- Restore applying or pruning policy outcomes.
- Restore running automatically without a host request.
