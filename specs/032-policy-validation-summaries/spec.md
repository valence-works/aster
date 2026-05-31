# Feature Specification: Policy Validation Summaries

**Feature Branch**: `032-policy-validation-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Add pure host-facing summaries for policy validation results, aggregating diagnostics deterministically by code, path, policy id, resource id, and resource version while preserving existing policy validation behavior and avoiding storage/provider/service changes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Validation Health (Priority: P1)

Hosts can turn a policy validation result into a compact health summary that reports whether validation passed, whether diagnostics exist, and how many diagnostics were produced.

**Why this priority**: This is the minimum useful reporting surface for hosts that need to display or log policy validation outcomes without inspecting every diagnostic manually.

**Independent Test**: Create successful and failing policy validation results, summarize them, and verify total diagnostic counts plus valid/invalid booleans.

**Acceptance Scenarios**:

1. **Given** a successful policy validation result, **When** the host summarizes it, **Then** the summary reports zero diagnostics, valid state, and no grouped counts.
2. **Given** a policy validation result with diagnostics, **When** the host summarizes it, **Then** the summary reports the total diagnostic count, invalid state, and that diagnostics are present.

---

### User Story 2 - Group Diagnostics Deterministically (Priority: P2)

Hosts can summarize policy validation diagnostics by stable diagnostic code, path, policy identifier, resource identifier, and resource version so dashboards and logs can highlight recurring validation issues.

**Why this priority**: Existing diagnostics are detailed but verbose. Deterministic grouped counts make validation output easier to compare across runs and easier to display in host tooling.

**Independent Test**: Create a validation result with mixed diagnostics and verify deterministic counts for each grouping dimension.

**Acceptance Scenarios**:

1. **Given** diagnostics with repeated codes and paths, **When** the host summarizes them, **Then** code and path counts are grouped and ordered deterministically.
2. **Given** diagnostics with policy identifiers, resource identifiers, and resource versions, **When** the host summarizes them, **Then** each non-empty grouping dimension has deterministic counts.
3. **Given** diagnostics with blank or missing string keys, **When** the host summarizes them, **Then** the total diagnostic count still includes those diagnostics while key-specific counts omit blank values.

---

### User Story 3 - Preserve Validation Behavior (Priority: P3)

Policy validation summary creation remains a pure reporting operation and does not change validation results, policy declarations, resource definitions, storage, providers, services, or execution behavior.

**Why this priority**: The slice should improve host observability without expanding the policy engine or changing existing policy semantics.

**Independent Test**: Run existing policy validation tests and full solution validation after adding summaries.

**Acceptance Scenarios**:

1. **Given** existing policy validation scenarios, **When** summaries are added, **Then** current validation behavior and diagnostics remain unchanged.
2. **Given** manually constructed validation results, **When** the host summarizes them, **Then** summary creation performs no storage access, provider access, registration, scanning, or mutation.

### Edge Cases

- Empty diagnostic collections produce zero counts and successful booleans.
- Missing nested diagnostic collections are treated as empty for summary purposes.
- Blank diagnostic code, path, policy identifier, and resource identifier values are excluded from key-specific counts but still contribute to the total diagnostic count.
- Resource version counts include only diagnostics that specify a resource version.
- Summary creation fails fast when the validation result itself is missing.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature is limited to pure aggregate views over existing validation results. It does not introduce policy engines, reporting frameworks, audit persistence, or workflow infrastructure.
- **Explicitness**: Hosts explicitly request a summary from a validation result. There is no automatic registration, runtime scanning, or implicit side effect.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, provider, or local development impact. Debugging remains straightforward because summaries are deterministic in-memory transformations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a policy validation summary that reports total diagnostic count, whether the result is valid, and whether diagnostics are present.
- **FR-002**: The summary MUST provide deterministic counts by non-empty diagnostic code.
- **FR-003**: The summary MUST provide deterministic counts by non-empty diagnostic path.
- **FR-004**: The summary MUST provide deterministic counts by non-empty policy identifier.
- **FR-005**: The summary MUST provide deterministic counts by non-empty resource identifier.
- **FR-006**: The summary MUST provide deterministic counts by resource version when a diagnostic includes one.
- **FR-007**: The summary MUST treat empty or missing diagnostic collections as empty.
- **FR-008**: The summary MUST fail fast when the validation result itself is missing.
- **FR-009**: The summary MUST NOT mutate validation results, diagnostics, policy declarations, resource definitions, resources, or activation state.
- **FR-010**: The feature MUST NOT introduce storage changes, provider changes, service registration, schedulers, audit persistence, public SQL, public `IQueryable<Resource>`, query planning, policy execution changes, or policy validation behavior changes.

### Key Entities

- **Policy Validation Summary**: Aggregate view over one policy validation result, including validity booleans, total diagnostic count, and deterministic diagnostic count lists.
- **Diagnostic Code Count**: Count of diagnostics for one stable diagnostic code.
- **Diagnostic Path Count**: Count of diagnostics for one diagnostic path.
- **Policy Identifier Count**: Count of diagnostics associated with one policy identifier.
- **Resource Identifier Count**: Count of diagnostics associated with one resource identifier.
- **Resource Version Count**: Count of diagnostics associated with one resource version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can summarize a successful validation result and receive zero counts with valid booleans.
- **SC-002**: Hosts can summarize a mixed diagnostic result and receive deterministic grouped counts by code, path, policy identifier, resource identifier, and resource version.
- **SC-003**: Blank or missing string grouping keys do not create grouped count entries while total diagnostic counts remain accurate.
- **SC-004**: Summary creation can be exercised with manually constructed validation results without service registration, provider setup, storage setup, or policy evaluation.
- **SC-005**: Existing policy validation tests continue to pass unchanged.
