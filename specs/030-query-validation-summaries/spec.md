# Feature Specification: Query Validation Summaries

**Feature Branch**: `030-query-validation-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Add pure host-facing summaries for query validation results so hosts can display deterministic failure counts by code, path, and feature without re-walking validation failures. Keep the slice bounded: no provider changes, no query planner, no service registration, no storage changes, no public SQL, no public IQueryable<Resource>, no execution behavior changes, and no new dependencies."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Validation Failure Codes (Priority: P1)

As a host author, I need a deterministic summary of query validation failure codes, so that preflight UI and logs can show why a query is unsupported without duplicating aggregation logic.

**Why this priority**: Stable failure-code counts are the primary host-facing view over query validation results.

**Independent Test**: Can be tested by creating a validation result with repeated and distinct failure codes and verifying total failure count, validity booleans, and deterministic code counts.

**Acceptance Scenarios**:

1. **Given** a valid query validation result, **When** the host creates a summary, **Then** the summary reports zero failures and indicates the query is valid.
2. **Given** invalid query validation failures with repeated codes, **When** the host creates a summary, **Then** the summary reports deterministic counts by failure code.

---

### User Story 2 - Summarize Failure Locations and Features (Priority: P2)

As a host author, I need deterministic counts by query path and feature category, so that validation messages can be grouped by the part of the query and the unsupported capability.

**Why this priority**: Code counts explain what failed; path and feature counts help hosts present actionable remediation.

**Independent Test**: Can be tested by creating failures with repeated paths and features, including blank optional values, and verifying deterministic nonblank path and feature counts.

**Acceptance Scenarios**:

1. **Given** failures with path values, **When** the host creates a summary, **Then** the summary reports deterministic counts by nonblank path.
2. **Given** failures with feature values, **When** the host creates a summary, **Then** the summary reports deterministic counts by nonblank feature.
3. **Given** failures with null or blank optional values, **When** the host creates a summary, **Then** blank path and feature values are ignored in those count lists.

---

### User Story 3 - Preserve Pure Validation Behavior (Priority: P3)

As a maintainer, I need query validation summaries to remain pure transformations over existing validation results, so that provider capabilities, query planning, and execution behavior remain unchanged.

**Why this priority**: Query validation sits on provider boundaries; this slice must not widen into planning or execution.

**Independent Test**: Can be tested by creating summaries from manually constructed validation results and running existing query validator tests unchanged.

**Acceptance Scenarios**:

1. **Given** existing query validation results, **When** summaries are created, **Then** no provider, store, service registration, planner, SQL, queryable surface, or execution behavior is required.
2. **Given** existing query validator tests, **When** the summary helpers are introduced, **Then** existing validator behavior remains unchanged.

### Edge Cases

- `QueryValidationResult.Success` produces a valid summary with zero counts.
- Null validation-result inputs throw normal argument validation errors.
- Null failure collections on manually constructed validation results are treated as empty where possible.
- Blank failure codes are ignored for code counts but still included in the total failure count.
- Blank paths and features are ignored in path and feature count lists.
- Count lists are ordered ordinally by key for deterministic host output.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The acceptable behavior is immutable summary records and pure extension helpers over existing validation results. Query planners, provider changes, and execution wrappers are out of scope.
- **Explicitness**: Hosts explicitly call `ToSummary()` on validation results. There is no hidden registration or runtime scanning.
- **Dependencies**: None.
- **Operational Impact**: No deployment, migration, provider setup, local development, debugging, or observability changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a host-facing summary for `QueryValidationResult`.
- **FR-002**: The query validation summary MUST expose total failure count.
- **FR-003**: The query validation summary MUST expose validity booleans derived from the validation result.
- **FR-004**: The query validation summary MUST expose deterministic counts by nonblank failure code.
- **FR-005**: The query validation summary MUST expose deterministic counts by nonblank failure path.
- **FR-006**: The query validation summary MUST expose deterministic counts by nonblank failure feature.
- **FR-007**: The summary helper MUST ignore blank code, path, and feature keys in key-specific count lists while preserving total failure count.
- **FR-008**: The summary helper MUST be a pure transformation over supplied validation result objects and MUST NOT read or write stores, call providers, register services, plan queries, execute queries, expose raw SQL, expose public `IQueryable<Resource>`, or mutate validation results.
- **FR-009**: The feature MUST preserve existing query validation and execution behavior.

### Key Entities *(include if feature involves data)*

- **Query Validation Summary**: Aggregate view over a query validation result, including total failure count, validity booleans, code counts, path counts, and feature counts.
- **Query Validation Failure Code Count**: Deterministic count for one stable validation failure code.
- **Query Validation Failure Path Count**: Deterministic count for one query path.
- **Query Validation Failure Feature Count**: Deterministic count for one feature category.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can create a summary from a valid validation result and observe zero failures with valid-state booleans.
- **SC-002**: Hosts can create a summary from invalid validation results and obtain correct deterministic counts by code, path, and feature.
- **SC-003**: Blank optional fields do not create empty-key count rows while total failure count remains accurate.
- **SC-004**: Existing query validator tests continue to pass unchanged except for focused tests added for summaries.
- **SC-005**: The feature introduces no new dependencies, storage changes, provider changes, service registrations, query planner, public SQL, public `IQueryable<Resource>`, or execution behavior changes.
