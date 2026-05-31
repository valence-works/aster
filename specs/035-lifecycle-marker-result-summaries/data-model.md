# Data Model: Lifecycle Marker Result Summaries

## Lifecycle Marker Result Summary

Aggregate view over one or more `ResourceLifecycleMarkerResult` objects.

Fields:

- `TotalResultCount`: Total number of supplied non-null results.
- `SucceededCount`: Number of results whose `Succeeded` property is true.
- `FailedCount`: Number of results whose `Succeeded` property is false.
- `MarkerPresentCount`: Number of results with a non-null marker.
- `MissingMarkerCount`: Number of results without a marker.
- `TotalDiagnosticCount`: Total nested diagnostic count across supplied results.
- `DistinctMarkerResourceCount`: Number of distinct nonblank marker resource identifiers.
- `IsFullySuccessful`: True when every supplied non-null result succeeded.
- `HasFailures`: True when one or more supplied result failed.
- `HasDiagnostics`: True when one or more nested diagnostics are present.
- `MarkerStateCounts`: Deterministic counts by marker state.
- `MarkerResourceCounts`: Deterministic counts by nonblank marker resource identifier.
- `DiagnosticCodeCounts`: Deterministic counts by nonblank diagnostic code.
- `DiagnosticPathCounts`: Deterministic counts by nonblank diagnostic path.
- `DiagnosticResourceIdCounts`: Deterministic counts by nonblank diagnostic resource identifier.

Validation and behavior:

- Single-result summary creation requires a non-null result.
- Enumerable summary creation treats a null result collection as empty.
- Null results inside an enumerable are ignored.
- Null nested diagnostic collections are treated as empty.
- Summary creation does not mutate result, marker, or diagnostic objects.
- Summary creation performs no I/O, service resolution, provider access, storage access, marker writes, policy evaluation, or lifecycle dispatch.

## Lifecycle Marker State Count

Count of result markers for one `ResourceLifecycleMarkerState`.

Fields:

- `State`: Marker state.
- `Count`: Number of markers with that state.

Ordering:

- Ordered by enum value.

## Lifecycle Marker Resource Count

Count of result markers for one nonblank marker resource identifier.

Fields:

- `ResourceId`: Nonblank resource identifier.
- `Count`: Number of markers with that resource identifier.

Ordering:

- Ordered by `ResourceId` using ordinal string comparison.

## Policy Diagnostic Path Count

Count of marker result diagnostics for one nonblank diagnostic path.

Fields:

- `Path`: Nonblank diagnostic path.
- `Count`: Number of diagnostics with that path.

Ordering:

- Ordered by `Path` using ordinal string comparison.

## Policy Diagnostic Resource Count

Count of marker result diagnostics for one nonblank diagnostic resource identifier.

Fields:

- `ResourceId`: Nonblank resource identifier.
- `Count`: Number of diagnostics with that resource identifier.

Ordering:

- Ordered by `ResourceId` using ordinal string comparison.

## State and Lifecycle

No persisted state is introduced. Summaries are ephemeral projections over supplied marker result objects.
