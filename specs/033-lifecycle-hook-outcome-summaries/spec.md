# Feature Specification: Lifecycle Hook Outcome Summaries

**Feature Branch**: `033-lifecycle-hook-outcome-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Add pure host-facing summaries for lifecycle hook outcomes, aggregating manually supplied hook outcomes deterministically by status, outcome code, diagnostic code, lifecycle point, and hook type while preserving existing hook dispatcher behavior and avoiding storage/provider/service changes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Hook Outcome Health (Priority: P1)

Hosts can summarize one or more lifecycle hook outcomes into total outcome counts, status counts, and clear success/failure booleans.

**Why this priority**: Hook outcomes are already structured, but host tests and diagnostics need a compact health view without inspecting each outcome manually.

**Independent Test**: Create continue, rejected, and failed hook outcomes, summarize them, and verify total counts plus success/failure booleans.

**Acceptance Scenarios**:

1. **Given** only continue outcomes, **When** the host summarizes them, **Then** the summary reports all outcomes as continuing and fully successful.
2. **Given** rejected or failed outcomes, **When** the host summarizes them, **Then** the summary reports non-success booleans and deterministic status counts.

---

### User Story 2 - Group Hook Diagnostics Deterministically (Priority: P2)

Hosts can summarize hook outcome codes and nested diagnostics by stable code, lifecycle point, and hook type so reporting can identify repeated hook issues.

**Why this priority**: Hook failures and diagnostics can come from different lifecycle points and hook implementations. Deterministic grouping makes this usable in host logs, tests, and dashboards.

**Independent Test**: Create mixed outcomes with repeated outcome codes and nested diagnostics, summarize them, and verify deterministic grouped counts.

**Acceptance Scenarios**:

1. **Given** outcomes with repeated outcome codes, **When** the host summarizes them, **Then** outcome-code counts are grouped and ordered deterministically.
2. **Given** diagnostics with repeated diagnostic codes, lifecycle points, and hook types, **When** the host summarizes them, **Then** each grouping dimension has deterministic counts.
3. **Given** blank outcome codes, diagnostic codes, or hook types, **When** the host summarizes them, **Then** total counts still include the outcomes/diagnostics while key-specific counts omit blank values.

---

### User Story 3 - Preserve Hook Execution Behavior (Priority: P3)

Summary creation remains a pure reporting operation and does not invoke hooks, resolve services, dispatch lifecycle operations, change exception behavior, or mutate outcome objects.

**Why this priority**: The slice is a host-reporting helper, not a lifecycle dispatcher change.

**Independent Test**: Run existing lifecycle hook dispatcher tests and full solution validation after adding summaries.

**Acceptance Scenarios**:

1. **Given** existing lifecycle hook dispatcher tests, **When** summaries are added, **Then** hook ordering, rejection, failure, and exception behavior remain unchanged.
2. **Given** manually constructed lifecycle hook outcomes, **When** the host summarizes them, **Then** summary creation performs no service resolution, hook invocation, storage access, provider access, or mutation.

### Edge Cases

- Empty or missing outcome collections produce zero counts and successful booleans.
- Missing nested diagnostic collections are treated as empty for summary purposes.
- Blank outcome code, diagnostic code, and hook type values are excluded from key-specific counts but still contribute to total outcome or diagnostic counts.
- Lifecycle point counts include only diagnostics because lifecycle point is diagnostic metadata.
- Summary creation fails fast when a single outcome input is missing.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature is limited to small immutable summary records and pure aggregation helpers. It does not introduce lifecycle reporting services, audit persistence, dispatcher changes, or hook execution infrastructure.
- **Explicitness**: Hosts explicitly call summary helpers on supplied outcomes. There is no automatic registration, runtime scanning, hook invocation, or hidden side effect.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, provider, service registration, or local development impact. Debugging remains straightforward because summaries are deterministic in-memory transformations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a lifecycle hook outcome summary that reports total outcome count and status counts.
- **FR-002**: The summary MUST report whether all supplied outcomes can continue and whether any rejected or failed outcomes are present.
- **FR-003**: The summary MUST provide deterministic counts by lifecycle hook outcome status.
- **FR-004**: The summary MUST provide deterministic counts by non-empty outcome code.
- **FR-005**: The summary MUST provide total nested diagnostic count across supplied outcomes.
- **FR-006**: The summary MUST provide deterministic counts by non-empty diagnostic code.
- **FR-007**: The summary MUST provide deterministic counts by diagnostic lifecycle point.
- **FR-008**: The summary MUST provide deterministic counts by non-empty diagnostic hook type.
- **FR-009**: The summary MUST treat empty or missing outcome collections as empty.
- **FR-010**: The summary MUST treat missing nested diagnostic collections as empty.
- **FR-011**: The summary MUST fail fast when a single lifecycle hook outcome input is missing.
- **FR-012**: The summary MUST NOT mutate hook outcomes, diagnostics, resources, resource versions, activation state, portable snapshots, or policy declarations.
- **FR-013**: The feature MUST NOT introduce storage changes, provider changes, service registration, scheduler behavior, audit persistence, public SQL, public `IQueryable<Resource>`, lifecycle dispatcher behavior changes, hook execution changes, or mutation behavior.

### Key Entities

- **Lifecycle Hook Outcome Summary**: Aggregate view over one or more lifecycle hook outcomes, including total counts, status counts, outcome-code counts, diagnostic counts, lifecycle-point counts, hook-type counts, and success/failure booleans.
- **Lifecycle Hook Outcome Status Count**: Count of outcomes for one outcome status.
- **Lifecycle Hook Outcome Code Count**: Count of outcomes for one non-empty outcome code.
- **Lifecycle Hook Diagnostic Code Count**: Count of nested diagnostics for one non-empty diagnostic code.
- **Lifecycle Hook Diagnostic Lifecycle Point Count**: Count of nested diagnostics for one lifecycle point.
- **Lifecycle Hook Diagnostic Hook Type Count**: Count of nested diagnostics for one non-empty hook type.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can summarize only-continue outcomes and receive fully successful booleans with deterministic status counts.
- **SC-002**: Hosts can summarize mixed continue/rejected/failed outcomes and receive deterministic status and outcome-code counts.
- **SC-003**: Hosts can summarize nested diagnostics and receive deterministic diagnostic-code, lifecycle-point, and hook-type counts.
- **SC-004**: Empty and null collection edge cases produce deterministic zero-count summaries.
- **SC-005**: Existing lifecycle hook dispatcher tests continue to pass unchanged.
