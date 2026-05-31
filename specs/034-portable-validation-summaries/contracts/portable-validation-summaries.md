# Contract: Portable Validation Summaries

## Public SDK Behavior

Portable snapshot validation results expose an explicit summary helper:

```csharp
PortableValidationSummary ToSummary(this PortableSnapshotValidationResult result)
```

The helper MUST:

- Throw `ArgumentNullException` when `result` is null.
- Treat a null `Diagnostics` collection as empty.
- Return a pure in-memory summary without service resolution, provider access, storage access, import, export, validation execution, or mutation.
- Preserve current portability validation, import, export, provider, storage, and service registration behavior unchanged.

## Summary Contract

`PortableValidationSummary` MUST expose:

- `IsValid`
- `HasErrors`
- `TotalDiagnosticCount`
- `DiagnosticSeverityCounts`
- `DiagnosticCodeCounts`

`IsValid` MUST be copied from `PortableSnapshotValidationResult.IsValid`.

`HasErrors` MUST be true when `DiagnosticSeverityCounts` includes `PortableDiagnosticSeverity.Error` with a count greater than zero.

`TotalDiagnosticCount` MUST include every diagnostic, including diagnostics with blank codes.

## Count Contracts

The validation summary MUST reuse existing portability count records where practical:

- `PortableDiagnosticSeverityCount`: `Severity`, `Count`
- `PortableDiagnosticCodeCount`: `Code`, `Count`

String-key diagnostic code counts MUST omit null, empty, or whitespace-only codes and sort remaining keys using ordinal comparison.

Severity counts MUST sort by enum value.

## Non-Goals

This feature MUST NOT introduce:

- Storage schema changes
- Provider behavior changes
- Service registration changes
- Runtime scanning or automatic discovery
- Reporting framework or dashboard APIs
- Export behavior changes
- Import behavior changes
- Validation behavior changes
- Audit persistence
- Public raw SQL
- Public `IQueryable<Resource>`
- Query planner behavior
- Resource, version, lifecycle marker, definition, activation, portability, or policy mutation
