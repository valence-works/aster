# Data Model: Policy Preview Summaries

## Policy Preview Summary

Aggregate view over one `ResourcePolicyEvaluationPreview`.

Fields:

- `TenantScope`: Effective tenant from the source preview.
- `EvaluationTimestamp`: Optional timestamp used by the source preview.
- `TotalCandidateCount`: Number of candidate outcomes.
- `DistinctResourceCount`: Distinct nonblank resource identifiers represented by candidates.
- `DistinctResourceVersionTargetCount`: Distinct `(resourceId, resourceVersion)` targets represented by candidates with a nonblank resource identifier and resource version.
- `HasDiagnostics`: True when at least one nonblank diagnostic code is present.
- `IsDiagnosticFree`: True when no nonblank diagnostic code is present.
- `OutcomeCounts`: Deterministic counts by `ResourcePolicyOutcome`.
- `KindCounts`: Deterministic counts by `ResourcePolicyKind`.
- `DiagnosticCodeCounts`: Deterministic diagnostic code counts across preview diagnostics.

Validation and invariants:

- Null source preview fails fast.
- Null candidate collection is treated as empty.
- Null diagnostic collection is treated as empty.
- Blank resource identifiers are ignored for distinct resource counts.
- Blank diagnostic codes are ignored.
- Outcome and kind counts are ordered by enum value.
- Diagnostic code counts are ordered by ordinal code.

## Policy Outcome Count

Count for one previewed `ResourcePolicyOutcome`.

Fields:

- `Outcome`: Previewed policy outcome.
- `Count`: Number of candidates with the outcome.

## Policy Kind Count

Count for one previewed `ResourcePolicyKind`.

Fields:

- `Kind`: Policy kind.
- `Count`: Number of candidates with the kind.

## State Transitions

None. Summaries are pure transformations over existing preview result objects and do not change resource versions, activation state, lifecycle marker state, policies, storage, providers, or service registrations.
