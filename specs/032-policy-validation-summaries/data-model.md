# Data Model: Policy Validation Summaries

## Resource Policy Validation Summary

Aggregate view over one `ResourcePolicyValidationResult`.

Fields:

- `TotalDiagnosticCount`: Total number of diagnostics in the result.
- `IsValid`: True when `TotalDiagnosticCount` is zero.
- `HasDiagnostics`: True when `TotalDiagnosticCount` is greater than zero.
- `DiagnosticCodeCounts`: Deterministic counts by nonblank diagnostic code.
- `DiagnosticPathCounts`: Deterministic counts by nonblank diagnostic path.
- `PolicyIdCounts`: Deterministic counts by nonblank policy identifier.
- `ResourceIdCounts`: Deterministic counts by nonblank resource identifier.
- `ResourceVersionCounts`: Deterministic counts by resource version.

Validation and behavior:

- Summary creation requires a non-null validation result.
- A null diagnostics collection is treated as empty.
- Summary creation does not mutate result or diagnostic objects.
- Summary creation performs no I/O, service resolution, provider access, storage access, or policy validation.

## Diagnostic Code Count

Count of diagnostics for one stable diagnostic code.

Fields:

- `Code`: Nonblank diagnostic code.
- `Count`: Number of diagnostics with that code.

Ordering:

- Ordered by `Code` using ordinal string comparison.

## Diagnostic Path Count

Count of diagnostics for one diagnostic path.

Fields:

- `Path`: Nonblank diagnostic path.
- `Count`: Number of diagnostics with that path.

Ordering:

- Ordered by `Path` using ordinal string comparison.

## Policy Identifier Count

Count of diagnostics for one policy identifier.

Fields:

- `PolicyId`: Nonblank policy identifier.
- `Count`: Number of diagnostics with that policy identifier.

Ordering:

- Ordered by `PolicyId` using ordinal string comparison.

## Resource Identifier Count

Count of diagnostics for one resource identifier.

Fields:

- `ResourceId`: Nonblank resource identifier.
- `Count`: Number of diagnostics with that resource identifier.

Ordering:

- Ordered by `ResourceId` using ordinal string comparison.

## Resource Version Count

Count of diagnostics for one resource version.

Fields:

- `ResourceVersion`: Resource version number.
- `Count`: Number of diagnostics with that version.

Ordering:

- Ordered by `ResourceVersion` ascending.

## State and Lifecycle

No persisted state is introduced. Summaries are ephemeral projections over existing result objects.
