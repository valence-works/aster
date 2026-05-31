# Data Model: Portable Validation Summaries

## Portable Validation Summary

Aggregate view over one `PortableSnapshotValidationResult`.

Fields:

- `IsValid`: Validation decision copied from the source result.
- `HasErrors`: True when one or more diagnostics has `PortableDiagnosticSeverity.Error`.
- `TotalDiagnosticCount`: Total number of validation diagnostics.
- `DiagnosticSeverityCounts`: Deterministic counts by `PortableDiagnosticSeverity`.
- `DiagnosticCodeCounts`: Deterministic counts by nonblank diagnostic code.

Validation and behavior:

- Summary creation requires a non-null `PortableSnapshotValidationResult`.
- Null `Diagnostics` is treated as empty.
- Blank diagnostic codes are excluded from code counts.
- Diagnostics with blank codes still contribute to total diagnostic and severity counts.
- Summary creation does not mutate the validation result or diagnostic objects.
- Summary creation performs no I/O, service resolution, provider access, storage access, import, export, validation, or mutation.

## Portable Diagnostic Severity Count

Existing count record reused by validation summaries.

Fields:

- `Severity`: Diagnostic severity.
- `Count`: Number of diagnostics with that severity.

Ordering:

- Ordered by enum value.

## Portable Diagnostic Code Count

Existing count record reused by validation summaries.

Fields:

- `Code`: Nonblank diagnostic code.
- `Count`: Number of diagnostics with that code.

Ordering:

- Ordered by `Code` using ordinal string comparison.

## State and Lifecycle

No persisted state is introduced. Summaries are ephemeral projections over supplied validation result objects.
