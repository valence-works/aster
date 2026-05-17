# Feature Specification: SQLite JSON Facet Sorting

**Feature Branch**: `007-sqlite-facet-sorting`  
**Created**: 2026-05-17  
**Status**: Draft  
**Input**: User description: "Add SQLite JSON facet sorting support so the SQLite provider can sort query results by facet values when a SortExpression specifies an AspectKey, update capabilities, validation, docs, and tests."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sort SQLite Results By Facet Values (Priority: P1)

As an application developer using the SQLite JSON provider, I want queries to sort by scalar facet values so SQLite-backed resource lists can be ordered by domain data such as title, price, or count.

**Why this priority**: Facet sorting is already part of the portable query model and supported by the in-memory provider. Adding it to SQLite closes a visible provider gap.

**Independent Test**: Seed SQLite resources with facet values, query with `SortExpression` using `AspectKey`, and verify result order.

**Acceptance Scenarios**:

1. **Given** resources with text facet values, **When** a query sorts ascending by that facet, **Then** SQLite returns resources in text facet order.
2. **Given** resources with numeric facet values, **When** a query sorts descending by that facet, **Then** SQLite returns resources in numeric facet order.
3. **Given** resources missing the sorted facet, **When** a facet sort runs, **Then** matching facet values sort first and missing values sort predictably after them.

---

### User Story 2 - Declare The New SQLite Capability (Priority: P1)

As a caller that uses query capability discovery or validation, I want SQLite JSON capabilities to report facet sorting support so preflight validation matches execution behavior.

**Why this priority**: Capability declarations are the public contract for provider behavior.

**Independent Test**: Inspect SQLite capabilities and validate a facet-sort query.

**Acceptance Scenarios**:

1. **Given** an application configured with SQLite JSON, **When** capabilities are inspected, **Then** facet sorting is reported as supported.
2. **Given** a facet-sort query for SQLite JSON, **When** shared validation runs, **Then** validation succeeds unless another unsupported shape is present.

### Edge Cases

- Sorted facet is absent on some resources.
- Sorted facet is numeric.
- Sorted facet is text.
- Multiple sorts still include stable resource id and version tie-breakers.
- Date-like facet ranges remain unsupported; this feature does not add date-like range filtering.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Reuse the existing SQLite JSON facet lookup and query builder path; do not introduce a planner or query framework.
- **Explicitness**: Capability support changes are visible through `SqliteJsonQueryCapabilitiesProvider`.
- **Dependencies**: None.
- **Operational Impact**: No new storage or deployment requirements.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: SQLite JSON query execution MUST support `SortExpression` with `AspectKey`.
- **FR-002**: Text facet sorting MUST order by the scalar facet value case-insensitively.
- **FR-003**: Numeric facet sorting MUST order by numeric value rather than text representation.
- **FR-004**: Resources missing the sorted facet SHOULD appear after resources with a sortable value.
- **FR-005**: SQLite JSON capabilities MUST declare facet sorting as supported.
- **FR-006**: Shared validation MUST accept SQLite facet-sort queries when no other unsupported query shape is present.
- **FR-007**: Date-like facet range filtering MUST remain unsupported.
- **FR-008**: The implementation MUST NOT add a query planner, public raw SQL API, public queryable API, storage migration, or runtime dependency.

### Key Entities

- **Facet Sort Expression**: A sort expression whose field identifies a facet and whose aspect key identifies the aspect payload.
- **SQLite Facet Value Expression**: Reusable SQLite JSON expression that resolves a facet value from a resource payload.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: SQLite JSON facet-sort tests pass for text and numeric facets.
- **SC-002**: SQLite JSON capabilities and validation tests report facet sorting as supported.
- **SC-003**: Existing provider conformance tests pass with SQLite facet sorting enabled.
- **SC-004**: Full solution test and build complete without warnings or errors.

## Assumptions

- Sorting is limited to scalar facet values in the existing JSON aspect shape.
- Missing facet values sort after present facet values.
- Stable tie-breakers remain resource id and version after requested sorts.
