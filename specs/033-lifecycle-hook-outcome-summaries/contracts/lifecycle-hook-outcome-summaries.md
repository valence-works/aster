# Contract: Lifecycle Hook Outcome Summaries

## Public SDK Behavior

Lifecycle hook outcomes expose explicit summary helpers:

```csharp
LifecycleHookOutcomeSummary ToSummary(this LifecycleHookOutcome outcome)
LifecycleHookOutcomeSummary ToSummary(this IEnumerable<LifecycleHookOutcome>? outcomes)
```

The helpers MUST:

- Throw `ArgumentNullException` when a single `outcome` is null.
- Treat a null `outcomes` enumerable as empty.
- Ignore null outcome entries inside an enumerable.
- Treat null nested diagnostic collections as empty.
- Return pure in-memory summaries without service resolution, provider access, storage access, lifecycle dispatch, hook invocation, or mutation.
- Preserve current lifecycle hook dispatcher behavior unchanged.

## Summary Contract

`LifecycleHookOutcomeSummary` MUST expose:

- `TotalOutcomeCount`
- `ContinueCount`
- `RejectedCount`
- `FailedCount`
- `TotalDiagnosticCount`
- `IsFullySuccessful`
- `HasRejectedOutcomes`
- `HasFailedOutcomes`
- `HasDiagnostics`
- `StatusCounts`
- `OutcomeCodeCounts`
- `DiagnosticCodeCounts`
- `DiagnosticLifecyclePointCounts`
- `DiagnosticHookTypeCounts`

`IsFullySuccessful` MUST be true when all supplied outcomes can continue. Empty outcome collections count as fully successful.

`HasDiagnostics` MUST be true when `TotalDiagnosticCount` is greater than zero.

## Count Contracts

Lifecycle hook outcome summary count records MUST expose:

- `LifecycleHookOutcomeStatusCount`: `Status`, `Count`
- `LifecycleHookOutcomeCodeCount`: `Code`, `Count`
- `LifecycleHookDiagnosticCodeCount`: `Code`, `Count`
- `LifecycleHookDiagnosticLifecyclePointCount`: `LifecyclePoint`, `Count`
- `LifecycleHookDiagnosticHookTypeCount`: `HookType`, `Count`

String-key counts MUST omit null, empty, or whitespace-only keys and sort remaining keys using ordinal comparison.

Enum-key counts MUST sort by enum value.

## Non-Goals

This feature MUST NOT introduce:

- Storage schema changes
- Provider behavior changes
- Service registration changes
- Runtime scanning or automatic discovery
- Schedulers
- Audit persistence
- Lifecycle dispatcher behavior changes
- Hook invocation behavior changes
- Hook exception behavior changes
- Public raw SQL
- Public `IQueryable<Resource>`
- Query planner behavior
- Resource, version, lifecycle marker, definition, activation, portability, or policy mutation
