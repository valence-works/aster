# Feature Specification: Portability Result Summaries

**Feature Branch**: `028-portability-result-summaries`
**Created**: 2026-05-31
**Status**: Draft
**Input**: Continue with the next bounded slice after policy preview summaries.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Portable Export Results (Priority: P1)

As a host developer rendering export feedback, I want a deterministic summary of portable export results so that operators can see exported entity counts, skipped activation entries, and diagnostics without recomputing them.

**Why this priority**: Export is the first portability operation. Hosts need a simple rollup before handing snapshots to operators or automation.

**Independent Test**: Construct an export result with a snapshot, skipped activation entries, and diagnostics, then verify entity counts, skipped count, diagnostic severity counts, diagnostic code counts, and success booleans.

**Acceptance Scenarios**:

1. **Given** an export result with a snapshot, **When** it is summarized, **Then** the summary reports definition, resource-version, activation-entry, and lifecycle-marker counts.
2. **Given** export skipped activation entries, **When** it is summarized, **Then** skipped activation count is reported.
3. **Given** export diagnostics, **When** it is summarized, **Then** severity and code counts are deterministic.

---

### User Story 2 - Summarize Portable Import Preview Results (Priority: P2)

As a host developer rendering import preview screens, I want a deterministic summary of portable import previews so that operators can see planned counts, identity mapping reasons, and blocking diagnostics before writes.

**Why this priority**: Import preview is the operator decision point before mutation. The summary should make planned scope and blockers easy to present.

**Independent Test**: Construct an import preview with planned counts, identity mappings, and diagnostics, then verify count totals, mapping reason counts, diagnostic counts, and importability booleans.

**Acceptance Scenarios**:

1. **Given** an import preview with planned counts, **When** it is summarized, **Then** the summary reports those counts and a total planned item count.
2. **Given** an import preview with identity mappings, **When** it is summarized, **Then** mapping reason counts are deterministic.
3. **Given** an import preview cannot import due to diagnostics, **When** it is summarized, **Then** diagnostic and can-import booleans reflect that state.

---

### User Story 3 - Summarize Portable Import Results (Priority: P3)

As a host developer rendering completed import feedback, I want a deterministic summary of portable import results so that operators can see actual imported counts, reuse/remap counts, status, and diagnostics.

**Why this priority**: Import result reporting should align with preview reporting while preserving existing import semantics.

**Independent Test**: Construct import results for imported, no-op, and failed outcomes, then verify status booleans, actual counts, mapping reason counts, and diagnostics.

**Acceptance Scenarios**:

1. **Given** a successful import result, **When** it is summarized, **Then** imported status and actual count totals are reported.
2. **Given** a no-op import result, **When** it is summarized, **Then** no-op status is reported without treating the result as failed.
3. **Given** a failed import result with diagnostics, **When** it is summarized, **Then** failed status and diagnostic counts are reported.

### Edge Cases

- Null result inputs MUST fail fast with argument validation.
- Null snapshots MUST produce zero exported entity counts.
- Null diagnostics, identity maps, and skipped activation collections MUST be treated as empty collections.
- Blank diagnostic codes MUST be ignored for diagnostic code counts.
- Diagnostic severity counts MUST be ordered deterministically by enum value.
- Identity mapping reason counts MUST be ordered deterministically by enum value.
- Summaries MUST NOT perform reads, writes, validation, import planning, export generation, storage access, provider access, background work, or query planning.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add pure extension helpers and records over existing portability result models only. No service, registry, provider, recipe package, workflow, or audit store is introduced.
- **Explicitness**: Callers explicitly summarize existing result objects. There is no hidden discovery, scanning, ambient state, or automatic reporting behavior.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, provider setup, debugging, observability, or runtime behavior changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a pure summary for portable snapshot export results.
- **FR-002**: Export summaries MUST preserve source tenant scope.
- **FR-003**: Export summaries MUST report exported definition, resource version, activation entry, lifecycle marker, and skipped activation counts.
- **FR-004**: Export summaries MUST expose whether a snapshot was produced and whether export diagnostics include errors.
- **FR-005**: The system MUST provide a pure summary for portable import previews.
- **FR-006**: Import preview summaries MUST preserve source and target tenant scopes.
- **FR-007**: Import preview summaries MUST report planned import counts and total planned item count.
- **FR-008**: Import preview summaries MUST report deterministic identity mapping reason counts.
- **FR-009**: Import preview summaries MUST expose whether import is allowed and whether diagnostics include errors.
- **FR-010**: The system MUST provide a pure summary for portable import results.
- **FR-011**: Import result summaries MUST preserve source and target tenant scopes and import status.
- **FR-012**: Import result summaries MUST report actual import counts and total actual item count.
- **FR-013**: Import result summaries MUST expose imported, no-op, and failed status booleans.
- **FR-014**: Portability summaries MUST report deterministic diagnostic severity and diagnostic code counts.
- **FR-015**: Portability summary helpers MUST fail fast for null result inputs.
- **FR-016**: Portability summary helpers MUST treat null snapshots and collections as empty for counting purposes.
- **FR-017**: Portability summary helpers MUST be usable over manually constructed result objects without services or providers.
- **FR-018**: The system MUST preserve existing export, validation, preview import, write import, lifecycle hook, tenant, provider, and storage behavior.
- **FR-019**: The system MUST NOT introduce storage changes, provider changes, service registration, recipe packages, background jobs, audit persistence, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, query planners, import planning changes, export changes, or mutation behavior.
- **FR-020**: Roadmap housekeeping MUST mark `027-policy-preview-summaries` as landed and identify `028-portability-result-summaries` as the active bounded slice.

### Key Entities

- **Portable Export Summary**: Aggregate counts and booleans for one export result.
- **Portable Import Preview Summary**: Aggregate planned counts, mapping counts, diagnostics, and booleans for one import preview.
- **Portable Import Summary**: Aggregate actual counts, mapping counts, diagnostics, and status booleans for one import result.
- **Portable Diagnostic Severity Count**: Deterministic count for one diagnostic severity.
- **Portable Diagnostic Code Count**: Deterministic count for one diagnostic code.
- **Portable Identity Mapping Reason Count**: Deterministic count for one mapping reason.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can summarize an export result with a snapshot and receive correct exported entity, skipped activation, and diagnostic counts.
- **SC-002**: A host can summarize an import preview and receive correct planned count totals, mapping reason counts, and diagnostic booleans.
- **SC-003**: A host can summarize imported, no-op, and failed import results and receive correct status booleans and counts.
- **SC-004**: Summary helpers can be called on manually constructed portability result objects without service registration or provider setup.
- **SC-005**: Existing portability, policy summary, lifecycle restore summary, and full test suites continue to pass unchanged.
