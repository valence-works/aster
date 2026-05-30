# Feature Specification: Batch Version History Inspection

**Feature Branch**: `023-batch-version-history`
**Created**: 2026-05-30
**Status**: Draft
**Input**: User request to proceed with the next bounded slice after policy application summaries.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect Selected Histories Together (Priority: P1)

As a host developer rendering maintenance or version-management screens, I want to request version histories for a small explicit set of resources in one call so that selected resources can be displayed consistently without manually coordinating repeated single-resource lookups.

**Why this priority**: This is the smallest useful increment beyond single-resource history inspection and directly supports host UI/reporting workflows.

**Independent Test**: Create several resources with different version and activation states, request their histories together, and verify each returned history matches the existing single-resource semantics.

**Acceptance Scenarios**:

1. **Given** two resources with multiple versions in the same tenant, **When** their identifiers are requested together, **Then** the result contains one ordered history per distinct requested resource identifier.
2. **Given** a requested resource has active channels and historical drafts, **When** batch history is requested, **Then** each version summary preserves latest, draft, active-channel, lifecycle, and maintenance-disposition information.
3. **Given** the request repeats the same resource identifier, **When** batch history is requested, **Then** the result contains one history for that identifier in first-seen request order.

---

### User Story 2 - Preserve Tenant Boundaries (Priority: P2)

As a multi-tenant host developer, I want batch history reads to stay within the effective tenant so that one tenant cannot see another tenant's resource history.

**Why this priority**: Tenant isolation is a core product invariant and must hold for any new read surface.

**Independent Test**: Store same or different resource identifiers in two tenants, request batch history for one tenant, and verify only that tenant's histories are returned.

**Acceptance Scenarios**:

1. **Given** matching resource identifiers exist in different tenants, **When** a batch history request specifies one tenant, **Then** every returned history uses the effective tenant and includes only that tenant's versions.
2. **Given** no tenant is supplied, **When** batch history is requested, **Then** the default single-tenant scope is used consistently with existing single-resource inspection.

---

### User Story 3 - Handle Empty and Missing Selections Predictably (Priority: P3)

As a host developer, I want empty, invalid, and missing-resource selections to behave predictably so that calling code can handle user selections without special provider-specific cases.

**Why this priority**: Predictable edge-case handling prevents callers from inferring behavior from storage implementation details.

**Independent Test**: Request empty selections, blank identifiers, and missing resources and verify deterministic validation and result behavior.

**Acceptance Scenarios**:

1. **Given** an empty resource identifier selection, **When** batch history is requested, **Then** the result succeeds with no histories.
2. **Given** a blank or whitespace resource identifier, **When** batch history is requested, **Then** the request fails fast with argument validation.
3. **Given** a non-existent resource identifier, **When** batch history is requested, **Then** the result includes an empty history for that requested identifier.

### Edge Cases

- Duplicate resource identifiers MUST be collapsed using ordinal comparison while preserving first-seen order.
- Empty selections MUST return an empty batch result instead of failing.
- Null request objects and blank resource identifiers MUST fail fast using argument validation.
- Missing resources MUST return empty histories for the requested identifiers, matching single-resource inspection behavior.
- Batch reads MUST NOT cross tenant boundaries or merge results across tenants.
- Batch reads MUST NOT expose public SQL, public `IQueryable<Resource>`, a query planner, provider registry, automatic discovery, or new storage requirements.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add a small batch inspection surface over existing history semantics; no provider registry, planner, persistence, or lifecycle workflow.
- **Explicitness**: Callers provide the exact resource identifiers and optional tenant scope; no hidden scanning or discovery is introduced.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, background job, or observability changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow callers to request version histories for an explicit collection of logical resource identifiers in one effective tenant.
- **FR-002**: The system MUST return histories in the first-seen order of distinct requested resource identifiers.
- **FR-003**: The system MUST collapse duplicate resource identifiers using ordinal comparison.
- **FR-004**: The system MUST preserve existing single-resource version summary semantics for every returned history, including version ordering, latest detection, draft detection, active channels, lifecycle marker state, pruning protection, and maintenance disposition.
- **FR-005**: The system MUST return an empty batch result when the requested identifier collection is empty.
- **FR-006**: The system MUST fail fast for null requests and blank or whitespace resource identifiers.
- **FR-007**: The system MUST include an empty history for each distinct requested identifier that has no stored versions in the effective tenant.
- **FR-008**: The system MUST apply tenant scoping consistently with existing version history inspection, including default single-tenant behavior when no tenant is supplied.
- **FR-009**: The system MUST preserve existing single-resource history inspection behavior and compatibility.
- **FR-010**: The system MUST NOT introduce storage schema changes, persisted summary records, provider registries, runtime scanning, automatic discovery, public SQL, public `IQueryable<Resource>`, background jobs, or a query planner.

### Key Entities

- **Batch Version History Request**: Caller-supplied tenant scope and explicit resource identifier selection.
- **Batch Version History Result**: Effective tenant and ordered histories for each distinct requested resource identifier.
- **Resource Version History**: Existing read-only history for one logical resource.
- **Resource Version Summary**: Existing read-only summary of one resource version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller can retrieve histories for at least three explicitly selected resources with one service call and receive one history per distinct requested identifier.
- **SC-002**: Batch results for a resource match the existing single-resource history result for the same tenant and resource identifier.
- **SC-003**: Duplicate identifiers in a request produce exactly one returned history while preserving the position of the first occurrence.
- **SC-004**: Empty and missing-resource selections complete deterministically without provider-specific exceptions.
- **SC-005**: Existing single-resource history tests continue to pass unchanged.
