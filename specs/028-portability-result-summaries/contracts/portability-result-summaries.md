# Contract: Portability Result Summaries

## Public SDK Behavior

The core SDK exposes pure summary helpers for portability result objects.

### Export Summary

`PortableSnapshotExportResult.ToSummary()` returns a `PortableExportSummary`.

Required behavior:

- Throws `ArgumentNullException` when the source result is null.
- Preserves `SourceTenantScope`.
- Counts snapshot definitions, resources, activation entries, and lifecycle markers when a snapshot exists.
- Reports zero snapshot counts when no snapshot exists.
- Counts skipped activation entries.
- Aggregates diagnostics by severity and nonblank code.

### Import Preview Summary

`PortableImportPreview.ToSummary()` returns a `PortableImportPreviewSummary`.

Required behavior:

- Throws `ArgumentNullException` when the source preview is null.
- Preserves `SourceTenantScope`, `TargetTenantScope`, and `CanImport`.
- Preserves planned count records.
- Reports total planned item count.
- Aggregates identity mappings by reason.
- Aggregates diagnostics by severity and nonblank code.

### Import Summary

`PortableImportResult.ToSummary()` returns a `PortableImportSummary`.

Required behavior:

- Throws `ArgumentNullException` when the source result is null.
- Preserves `SourceTenantScope`, `TargetTenantScope`, and `Status`.
- Preserves actual count records.
- Reports total actual item count.
- Exposes imported, no-op, and failed status booleans.
- Aggregates identity mappings by reason.
- Aggregates diagnostics by severity and nonblank code.

## Non-Goals

This contract does not add:

- New export behavior.
- New import planning behavior.
- New import write behavior.
- Service registration.
- Storage or provider behavior.
- Recipe packages.
- Audit persistence.
- Reporting infrastructure.
- Query planning.
- Public SQL.
- Public `IQueryable<Resource>`.
- Runtime scanning or automatic discovery.
- Background jobs or schedulers.
