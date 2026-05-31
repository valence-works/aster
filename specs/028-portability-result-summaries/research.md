# Research: Portability Result Summaries

## Decision: Use Pure Extension Helpers

Use extension methods over `PortableSnapshotExportResult`, `PortableImportPreview`, and `PortableImportResult`.

**Rationale**: The feature only aggregates data already present on result objects. A service would add registration and lifetime surface without reading external state or coordinating collaborators.

**Alternatives considered**:

- Service-based summarization: rejected because there is no dependency to inject and no provider state to access.
- Computed properties on result records only: rejected because summaries combine multiple grouped counts and the existing pattern uses explicit `ToSummary` helpers.

## Decision: Keep Export, Preview, and Import Summaries Separate

Expose one summary record per portability operation result shape.

**Rationale**: Export, preview, and import have different source fields and status semantics. Separate records keep behavior explicit and avoid a vague generic operation summary.

**Alternatives considered**:

- One generic portability summary: rejected because export lacks target tenant and import status, while import lacks skipped activation entries.
- Summarize validation results in this slice too: deferred to keep the slice focused on host operation results.

## Decision: Count Diagnostics by Severity and Code

Summaries expose deterministic severity counts and diagnostic code counts.

**Rationale**: Hosts need to distinguish informational, warning, and error diagnostics and still show stable code-level troubleshooting detail.

**Alternatives considered**:

- Code counts only: rejected because error presence is an operator-critical concept.
- Severity counts only: rejected because hosts need stable diagnostic detail.

## Decision: Count Identity Mappings by Reason

Import preview and import summaries group identity maps by `PortableIdentityMappingReason`.

**Rationale**: Mapping reason is the compact operator-facing explanation of preserved, reused, remapped, or collided identities.

**Alternatives considered**:

- Count mappings by entity kind: useful later, but reason counts better explain risk and actionability for this slice.
- Count every mapping only as a total: rejected because it hides remap/collision behavior.

## Decision: Null Collections Are Empty

Summary helpers fail fast for null result objects but treat null snapshots and collections as empty for counting.

**Rationale**: This matches existing summary behavior and keeps manually constructed test/UI DTOs easy to summarize while still catching programming errors for null result inputs.

**Alternatives considered**:

- Throw for null collections: rejected because result records are host-constructible and existing summary helpers already tolerate null collections.
