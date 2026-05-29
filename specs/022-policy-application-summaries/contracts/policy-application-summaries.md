# Contract: Policy Application Summaries

Policy application summaries are pure, host-facing aggregate views over existing result objects.

## Public SDK Behavior

The feature adds summary generation for:

```csharp
ResourcePolicyApplicationResult
ResourcePolicyPruningApplicationResult
```

Expected helper shape:

```csharp
ResourcePolicyApplicationSummary ToSummary();
ResourcePolicyPruningApplicationSummary ToSummary();
```

The exact member placement may follow existing SDK style, but summary generation must remain explicit and provider-free.

## Application Summary Rules

For marker-based policy application:

- `TotalCount` equals the number of candidate results.
- `AppliedCount`, `AlreadySatisfiedCount`, `SkippedCount`, and `FailedCount` reflect candidate statuses.
- `HasFailures` is true when `FailedCount > 0`.
- `IsFullySuccessful` is true only when every candidate is applied or already satisfied.
- `AffectedResourceCount` counts distinct non-blank resource IDs from applied and already satisfied candidates.
- `DiagnosticCodeCounts` groups non-blank diagnostic codes across all candidate diagnostics in ordinal code order.

## Pruning Summary Rules

For policy pruning application:

- `TotalCount` equals the number of candidate results.
- `PrunedCount`, `AlreadyPrunedCount`, `SkippedCount`, and `FailedCount` reflect candidate statuses.
- `HasFailures` is true when `FailedCount > 0`.
- `IsFullySuccessful` is true only when every candidate is pruned or already pruned.
- `AffectedTargetCount` counts distinct resource/version pairs from pruned and already pruned candidates.
- `DiagnosticCodeCounts` groups non-blank diagnostic codes across all candidate diagnostics in ordinal code order.

## Non-Goals

Summary generation does not:

- apply policies;
- re-evaluate policy criteria;
- mutate resources, markers, activation state, definitions, or versions;
- write audit records;
- query stores or providers;
- invoke lifecycle hooks;
- schedule background jobs;
- introduce public SQL or public `IQueryable<Resource>`;
- replace existing per-candidate results.
