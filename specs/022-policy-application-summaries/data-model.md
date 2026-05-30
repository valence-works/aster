# Data Model: Policy Application Summaries

## Policy Application Summary

Aggregate view over a `ResourcePolicyApplicationResult`.

Fields:

- Tenant scope.
- Applied timestamp.
- Total candidate count.
- Applied count.
- Already satisfied count.
- Skipped count.
- Failed count.
- Has failures flag.
- Fully successful flag.
- Distinct affected resource count.
- Diagnostic code counts.

Rules:

- Applied and already satisfied candidates count as successful.
- Skipped candidates are not failures, but they prevent the summary from being fully successful.
- Failed candidates set the failure flag.
- Affected resource count includes distinct non-blank resource IDs from applied and already satisfied candidates only.

## Policy Pruning Application Summary

Aggregate view over a `ResourcePolicyPruningApplicationResult`.

Fields:

- Tenant scope.
- Optional applied timestamp.
- Total candidate count.
- Pruned count.
- Already pruned count.
- Skipped count.
- Failed count.
- Has failures flag.
- Fully successful flag.
- Distinct affected target count.
- Diagnostic code counts.

Rules:

- Pruned and already pruned candidates count as successful.
- Skipped candidates are not failures, but they prevent the summary from being fully successful.
- Failed candidates set the failure flag.
- Affected target count includes distinct resource/version pairs from pruned and already pruned candidates only.

## Diagnostic Code Count

Deterministic count of a policy diagnostic code.

Fields:

- Code.
- Count.

Rules:

- Null, empty, and whitespace-only codes are ignored.
- Counts are ordered by code using ordinal comparison.
- Counts include diagnostics from failed and skipped candidates when present.
