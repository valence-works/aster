# Feature Specification: Index Projection Summaries

**Feature Branch**: `031-index-projection-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Add pure host-facing summaries for index projection validation and evaluation results so provider authors and hosts can display deterministic counts by failure code, field name, source, field type, and successful value counts without re-walking projection results. Keep the slice bounded: no physical indexes, no provider changes, no query planner, no service registration, no storage changes, no public SQL, no public IQueryable<Resource>, no execution behavior changes, and no new dependencies."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Projection Validation Failures (Priority: P1)

As a provider author, I need a deterministic summary of projection declaration validation failures, so that invalid provider-declared projections can be reported by failure code, field name, and source without duplicating aggregation logic.

**Why this priority**: Projection declaration validation is the first feedback loop for provider authors; stable aggregate failure counts make invalid declarations easier to diagnose.

**Independent Test**: Can be tested by creating a validation result with repeated failure codes, field names, and source descriptions, then verifying deterministic count lists and validity booleans.

**Acceptance Scenarios**:

1. **Given** a successful projection validation result, **When** a summary is created, **Then** it reports zero failures and valid-state booleans.
2. **Given** validation failures with repeated codes and fields, **When** a summary is created, **Then** it reports deterministic counts by nonblank code and nonblank field name.
3. **Given** validation failures with source descriptions, **When** a summary is created, **Then** it reports deterministic counts by nonblank source.

---

### User Story 2 - Summarize Projection Evaluation Results (Priority: P2)

As a host author, I need a deterministic summary of projection evaluation results, so that indexing or diagnostics screens can show successful value counts by field type alongside failure counts.

**Why this priority**: Evaluation results contain both successful values and structured failures; hosts need both sides to understand projection health.

**Independent Test**: Can be tested by creating an evaluation result with successful projection values and failures, then verifying total value/failure counts, field-type counts, field-name counts, and failure counts.

**Acceptance Scenarios**:

1. **Given** successful projection values with repeated field types, **When** a summary is created, **Then** it reports deterministic counts by field type.
2. **Given** projection evaluation failures, **When** a summary is created, **Then** it reports deterministic failure counts by code, field name, and source.
3. **Given** an evaluation result with both values and failures, **When** a summary is created, **Then** it reports both success and failure totals without discarding either side.

---

### User Story 3 - Preserve Pure Indexing Behavior (Priority: P3)

As a maintainer, I need projection summaries to remain pure transformations over existing result objects, so that provider declarations, projection evaluation, query validation, and query execution remain unchanged.

**Why this priority**: Projection summaries are reporting helpers; they must not imply physical indexes, query planning, or provider-specific behavior.

**Independent Test**: Can be tested by creating summaries from manually constructed results and running existing projection validation/evaluation tests unchanged.

**Acceptance Scenarios**:

1. **Given** existing projection validation and evaluation result objects, **When** summaries are created, **Then** no provider, store, service registration, physical index, planner, SQL, queryable surface, or execution behavior is required.
2. **Given** existing projection validation and evaluation tests, **When** the summary helpers are introduced, **Then** existing behavior remains unchanged.

### Edge Cases

- Successful validation and evaluation results produce zero failure counts.
- Evaluation results with zero values and zero failures report both totals as zero and valid-state booleans as valid.
- Null validation or evaluation result inputs throw normal argument validation errors.
- Null nested value/failure collections on manually constructed result objects are treated as empty where possible.
- Blank failure codes, field names, and source descriptions are ignored in key-specific count lists while preserving total failure count.
- Count lists are ordered deterministically for host output and snapshot tests.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The acceptable behavior is immutable summary records and pure extension helpers over existing projection results. Physical index creation, query planning, provider changes, and execution wrappers are out of scope.
- **Explicitness**: Hosts explicitly call `ToSummary()` on projection validation/evaluation results. There is no hidden registration, runtime scanning, or provider discovery.
- **Dependencies**: None.
- **Operational Impact**: No deployment, migration, provider setup, local development, debugging, or observability changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a host-facing summary for `IndexProjectionValidationResult`.
- **FR-002**: The projection validation summary MUST expose total failure count and validity booleans.
- **FR-003**: The projection validation summary MUST expose deterministic counts by nonblank failure code, field name, and source.
- **FR-004**: The system MUST provide a host-facing summary for `IndexProjectionEvaluationResult`.
- **FR-005**: The projection evaluation summary MUST expose total successful value count, total failure count, and validity booleans.
- **FR-006**: The projection evaluation summary MUST expose deterministic counts by successful value field type and field name.
- **FR-007**: The projection evaluation summary MUST expose deterministic failure counts by nonblank failure code, field name, and source.
- **FR-008**: Summary helpers MUST ignore blank code, field-name, and source keys in key-specific count lists while preserving total failure counts.
- **FR-009**: Summary helpers MUST be pure transformations over supplied result objects and MUST NOT read or write stores, call providers, register services, create physical indexes, plan queries, execute queries, expose raw SQL, expose public `IQueryable<Resource>`, or mutate result objects.
- **FR-010**: The feature MUST preserve existing projection validation, projection evaluation, query validation, and query execution behavior.

### Key Entities *(include if feature involves data)*

- **Index Projection Validation Summary**: Aggregate view over projection declaration validation failures, including total failures, validity booleans, and deterministic failure counts.
- **Index Projection Evaluation Summary**: Aggregate view over projection evaluation values and failures, including success counts, failure counts, validity booleans, field-type counts, field-name counts, and failure counts.
- **Projection Failure Code Count**: Deterministic count for one stable projection failure code.
- **Projection Failure Field Count**: Deterministic count for one projection field name associated with failures.
- **Projection Failure Source Count**: Deterministic count for one projection source description associated with failures.
- **Projection Value Field Type Count**: Deterministic count for one successful value field type.
- **Projection Value Field Count**: Deterministic count for one successful value field name.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Provider authors can create a validation summary and obtain correct deterministic failure counts by code, field name, and source.
- **SC-002**: Hosts can create an evaluation summary and obtain correct deterministic successful value counts by field type and field name.
- **SC-003**: Evaluation summaries preserve both success and failure totals when a result contains both values and failures.
- **SC-004**: Existing projection validation, projection evaluation, query validation, and full solution tests continue to pass unchanged except for focused tests added for summaries.
- **SC-005**: The feature introduces no new dependencies, storage changes, provider changes, service registrations, physical indexes, query planner, public SQL, public `IQueryable<Resource>`, or execution behavior changes.
