# Data Model: Lifecycle Restore Summaries

## Lifecycle Restore Application Summary

Aggregate view over one `ResourceLifecycleRestoreApplicationResult`.

Fields:

- `TenantScope`: Effective tenant from the source result.
- `RestoredAt`: Optional host timestamp from the source application result.
- `TotalCount`: Number of candidate results.
- `RestoredCount`: Number of candidates with `Restored` status.
- `AlreadyRestoredCount`: Number of candidates with `AlreadyRestored` status.
- `SkippedCount`: Number of candidates with `Skipped` status.
- `FailedCount`: Number of candidates with `Failed` status.
- `HasFailures`: True when `FailedCount` is greater than zero.
- `IsFullySuccessful`: True when every candidate is `Restored` or `AlreadyRestored`.
- `AffectedResourceCount`: Distinct nonblank resource identifiers from `Restored` and `AlreadyRestored` candidates.
- `DiagnosticCodeCounts`: Deterministic diagnostic code counts across candidate diagnostics.

Validation and invariants:

- Null source result fails fast.
- Null candidate collection is treated as empty.
- Null candidate diagnostic collection is treated as empty.
- Blank resource identifiers are ignored for `AffectedResourceCount`.
- Blank diagnostic codes are ignored.
- Diagnostic code counts are ordered by ordinal code.

## Lifecycle Restore Preview Summary

Aggregate view over one `ResourceLifecycleRestorePreviewResult`.

Fields:

- `TenantScope`: Effective tenant from the source result.
- `TotalCount`: Number of candidate results.
- `RestorableCount`: Number of candidates with `Restorable` status.
- `AlreadyRestoredCount`: Number of candidates with `AlreadyRestored` status.
- `SkippedCount`: Number of candidates with `Skipped` status.
- `FailedCount`: Number of candidates with `Failed` status.
- `HasFailures`: True when `FailedCount` is greater than zero.
- `IsFullySuccessful`: True when every candidate is `Restorable` or `AlreadyRestored`.
- `CandidateResourceCount`: Distinct nonblank resource identifiers from `Restorable` and `AlreadyRestored` candidates.
- `DiagnosticCodeCounts`: Deterministic diagnostic code counts across candidate diagnostics.

Validation and invariants:

- Null source result fails fast.
- Null candidate collection is treated as empty.
- Null candidate diagnostic collection is treated as empty.
- Blank resource identifiers are ignored for `CandidateResourceCount`.
- Blank diagnostic codes are ignored.
- Diagnostic code counts are ordered by ordinal code.

## Restore Diagnostic Code Count

Count for one stable diagnostic code observed in candidate diagnostics.

Fields:

- `Code`: Stable diagnostic code.
- `Count`: Number of diagnostics with the code.

Validation and invariants:

- Counts include only nonblank diagnostic codes.
- Counts are deterministic and ordered by code.

## State Transitions

None. Summaries are pure transformations over existing result objects and do not change resource versions, activation state, lifecycle marker state, policies, storage, providers, or service registrations.
