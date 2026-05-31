# Data Model: Portability Result Summaries

## Portable Export Summary

Aggregate view over one `PortableSnapshotExportResult`.

Fields:

- `SourceTenantScope`: Source tenant from the export result.
- `HasSnapshot`: True when a snapshot is present.
- `HasErrors`: True when diagnostics include at least one error severity.
- `DefinitionCount`: Number of definition snapshots exported.
- `ResourceVersionCount`: Number of resource version snapshots exported.
- `ActivationEntryCount`: Number of activation entries exported.
- `LifecycleMarkerCount`: Number of lifecycle markers exported.
- `SkippedActivationEntryCount`: Number of activation entries skipped during export.
- `DiagnosticSeverityCounts`: Deterministic counts by diagnostic severity.
- `DiagnosticCodeCounts`: Deterministic counts by diagnostic code.

## Portable Import Preview Summary

Aggregate view over one `PortableImportPreview`.

Fields:

- `SourceTenantScope`: Source tenant recorded in the snapshot.
- `TargetTenantScope`: Target tenant planned for import.
- `CanImport`: Whether the preview allows import.
- `HasErrors`: True when diagnostics include at least one error severity.
- `Counts`: Planned import counts from the preview.
- `TotalPlannedItemCount`: Sum of planned definition, resource, resource version, activation entry, and lifecycle marker counts.
- `MappingReasonCounts`: Deterministic counts by identity mapping reason.
- `DiagnosticSeverityCounts`: Deterministic counts by diagnostic severity.
- `DiagnosticCodeCounts`: Deterministic counts by diagnostic code.

## Portable Import Summary

Aggregate view over one `PortableImportResult`.

Fields:

- `SourceTenantScope`: Source tenant recorded in the snapshot.
- `TargetTenantScope`: Target tenant used for import.
- `Status`: Import status.
- `IsImported`: True when status is imported.
- `IsNoOp`: True when status is no-op.
- `IsFailed`: True when status is failed.
- `HasErrors`: True when diagnostics include at least one error severity.
- `Counts`: Actual import counts from the result.
- `TotalActualItemCount`: Sum of actual definition, resource, resource version, activation entry, and lifecycle marker counts.
- `MappingReasonCounts`: Deterministic counts by identity mapping reason.
- `DiagnosticSeverityCounts`: Deterministic counts by diagnostic severity.
- `DiagnosticCodeCounts`: Deterministic counts by diagnostic code.

## Portable Diagnostic Severity Count

Count for one `PortableDiagnosticSeverity`.

Fields:

- `Severity`: Diagnostic severity.
- `Count`: Number of diagnostics with the severity.

## Portable Diagnostic Code Count

Count for one stable portability diagnostic code.

Fields:

- `Code`: Stable diagnostic code.
- `Count`: Number of diagnostics with the code.

## Portable Identity Mapping Reason Count

Count for one `PortableIdentityMappingReason`.

Fields:

- `Reason`: Identity mapping reason.
- `Count`: Number of mappings with the reason.

## State Transitions

None. Summaries are pure transformations over existing result objects and do not change snapshots, imports, exports, storage, providers, lifecycle hooks, or service registrations.
