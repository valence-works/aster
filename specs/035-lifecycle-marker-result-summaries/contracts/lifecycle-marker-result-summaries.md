# Contract: Lifecycle Marker Result Summaries

## Public SDK Behavior

Lifecycle marker write results expose explicit summary helpers:

```csharp
ResourceLifecycleMarkerResultSummary ToSummary(this ResourceLifecycleMarkerResult result)
ResourceLifecycleMarkerResultSummary ToSummary(this IEnumerable<ResourceLifecycleMarkerResult>? results)
```

The helpers MUST:

- Throw `ArgumentNullException` when a single `result` is null.
- Treat a null `results` enumerable as empty.
- Ignore null result entries inside an enumerable.
- Treat null nested diagnostic collections as empty.
- Return pure in-memory summaries without service resolution, provider access, storage access, marker writes, policy evaluation, lifecycle dispatch, or mutation.
- Preserve current lifecycle marker service and store behavior unchanged.

## Summary Contract

`ResourceLifecycleMarkerResultSummary` MUST expose:

- `TotalResultCount`
- `SucceededCount`
- `FailedCount`
- `MarkerPresentCount`
- `MissingMarkerCount`
- `TotalDiagnosticCount`
- `DistinctMarkerResourceCount`
- `IsFullySuccessful`
- `HasFailures`
- `HasDiagnostics`
- `MarkerStateCounts`
- `MarkerResourceCounts`
- `DiagnosticCodeCounts`
- `DiagnosticPathCounts`
- `DiagnosticResourceIdCounts`

`IsFullySuccessful` MUST be true when every supplied non-null result succeeded. Empty result collections count as fully successful.

`HasDiagnostics` MUST be true when `TotalDiagnosticCount` is greater than zero.

## Count Contracts

Lifecycle marker result summary count records MUST expose:

- `ResourceLifecycleMarkerStateCount`: `State`, `Count`
- `ResourceLifecycleMarkerResourceCount`: `ResourceId`, `Count`
- `ResourcePolicyDiagnosticPathCount`: `Path`, `Count`
- `ResourcePolicyDiagnosticResourceIdCount`: `ResourceId`, `Count`

Diagnostic code counts SHOULD reuse `ResourcePolicyDiagnosticCodeCount`.

String-key counts MUST omit null, empty, or whitespace-only keys and sort remaining keys using ordinal comparison.

Enum-key counts MUST sort by enum value.

## Non-Goals

This feature MUST NOT introduce:

- Storage schema changes
- Provider behavior changes
- Service registration changes
- Runtime scanning or automatic discovery
- Reporting framework or dashboard APIs
- Lifecycle marker service behavior changes
- Lifecycle marker store behavior changes
- Policy evaluation behavior changes
- Lifecycle dispatcher behavior changes
- Audit persistence
- Public raw SQL
- Public `IQueryable<Resource>`
- Query planner behavior
- Resource, version, lifecycle marker, definition, activation, portability, or policy mutation
