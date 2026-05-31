# Data Model: Lifecycle Hook Outcome Summaries

## Lifecycle Hook Outcome Summary

Aggregate view over one or more `LifecycleHookOutcome` objects.

Fields:

- `TotalOutcomeCount`: Total number of supplied outcomes.
- `ContinueCount`: Number of outcomes with continue status.
- `RejectedCount`: Number of outcomes with rejected status.
- `FailedCount`: Number of outcomes with failed status.
- `TotalDiagnosticCount`: Total nested diagnostic count across supplied outcomes.
- `IsFullySuccessful`: True when every supplied outcome can continue.
- `HasRejectedOutcomes`: True when one or more outcomes are rejected.
- `HasFailedOutcomes`: True when one or more outcomes failed.
- `HasDiagnostics`: True when one or more nested diagnostics are present.
- `StatusCounts`: Deterministic counts by outcome status.
- `OutcomeCodeCounts`: Deterministic counts by nonblank outcome code.
- `DiagnosticCodeCounts`: Deterministic counts by nonblank diagnostic code.
- `DiagnosticLifecyclePointCounts`: Deterministic counts by diagnostic lifecycle point.
- `DiagnosticHookTypeCounts`: Deterministic counts by nonblank diagnostic hook type.

Validation and behavior:

- Single-outcome summary creation requires a non-null outcome.
- Enumerable summary creation treats a null outcome collection as empty.
- Null outcomes inside an enumerable are ignored.
- Null nested diagnostic collections are treated as empty.
- Summary creation does not mutate outcome or diagnostic objects.
- Summary creation performs no I/O, service resolution, provider access, storage access, hook invocation, or lifecycle dispatch.

## Lifecycle Hook Outcome Status Count

Count of outcomes for one `LifecycleHookOutcomeStatus`.

Fields:

- `Status`: Outcome status.
- `Count`: Number of outcomes with that status.

Ordering:

- Ordered by enum value.

## Lifecycle Hook Outcome Code Count

Count of outcomes for one nonblank outcome code.

Fields:

- `Code`: Nonblank outcome code.
- `Count`: Number of outcomes with that code.

Ordering:

- Ordered by `Code` using ordinal string comparison.

## Lifecycle Hook Diagnostic Code Count

Count of nested diagnostics for one nonblank diagnostic code.

Fields:

- `Code`: Nonblank diagnostic code.
- `Count`: Number of diagnostics with that code.

Ordering:

- Ordered by `Code` using ordinal string comparison.

## Lifecycle Hook Diagnostic Lifecycle Point Count

Count of nested diagnostics for one lifecycle point.

Fields:

- `LifecyclePoint`: Lifecycle point associated with diagnostics.
- `Count`: Number of diagnostics with that lifecycle point.

Ordering:

- Ordered by enum value.

## Lifecycle Hook Diagnostic Hook Type Count

Count of nested diagnostics for one nonblank hook type.

Fields:

- `HookType`: Nonblank hook type.
- `Count`: Number of diagnostics with that hook type.

Ordering:

- Ordered by `HookType` using ordinal string comparison.

## State and Lifecycle

No persisted state is introduced. Summaries are ephemeral projections over supplied outcome objects.
