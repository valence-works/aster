# Feature Specification: Provider Validation Execution Alignment

**Feature Branch**: `004-provider-validation-execution`  
**Created**: 2026-05-15  
**Status**: Draft  
**Input**: User description: "Align provider query execution with declared query capabilities and preflight validation. Providers should be able to reuse shared validation before execution, execution should remain authoritative, unsupported query failures should be consistent and actionable, and custom providers without declared capabilities should fail closed instead of accidentally validating against stale defaults."

## Clarifications

### Session 2026-05-15

- Q: What consistency shape should execution failures expose? → A: Execution failures expose stable code/category plus message.
- Q: How should validation match the active provider to its capability declaration? → A: Match active provider and capabilities by explicit provider key.
- Q: How should providers use shared validation during execution? → A: Execution runs shared validation first, then keeps provider-specific checks.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Execute With Consistent Unsupported-Query Feedback (Priority: P1)

As an application developer executing resource queries, I want provider execution failures to match preflight validation categories, so that skipped validation and failed execution produce the same actionable explanation for unsupported query shapes.

**Why this priority**: Execution is the final authority. If execution and validation disagree, developers cannot trust preflight results or error handling.

**Independent Test**: Can be fully tested by sending unsupported query shapes directly to provider execution and confirming the failure category, message, and affected query feature match the corresponding validation result.

**Acceptance Scenarios**:

1. **Given** a SQLite-backed application and a query with unsupported facet sorting, **When** the query is executed without calling validation first, **Then** execution fails with an actionable unsupported-query failure that identifies facet sorting.
2. **Given** a query with multiple unsupported features, **When** the query is preflighted and then executed, **Then** both paths identify the same unsupported feature categories even though execution may stop at the first blocking failure.
3. **Given** a provider supports a query shape, **When** the query is preflighted and executed, **Then** validation succeeds and execution proceeds without additional unsupported-query failures.

---

### User Story 2 - Fail Closed For Providers Without Declared Capabilities (Priority: P1)

As a host developer replacing or adding a query provider, I want validation to detect missing capability declarations for the active provider, so that applications do not accidentally validate against a previous provider's stale defaults.

**Why this priority**: Provider-agnostic behavior depends on active provider capabilities being explicit. Silent reuse of another provider's capabilities can make unsupported production behavior appear safe.

**Independent Test**: Can be fully tested by registering a custom query provider without matching capabilities and confirming validation fails closed with a capabilities-not-declared result.

**Acceptance Scenarios**:

1. **Given** an application replaces the default query provider without declaring capabilities, **When** a query is validated, **Then** validation fails with a capabilities-not-declared failure.
2. **Given** an application registers a provider and matching capability declaration using the same explicit provider key, **When** a supported query is validated, **Then** validation uses that provider's declaration rather than an earlier default declaration.
3. **Given** multiple providers are registered during host setup, **When** a query is validated, **Then** the active provider's capabilities are used deterministically.

---

### User Story 3 - Keep Provider Execution Authoritative (Priority: P2)

As a library maintainer, I want preflight validation to reduce duplicate unsupported-shape logic without weakening provider execution safeguards, so that query services remain safe even when validation is skipped or stale.

**Why this priority**: Shared validation improves consistency, but execution must still protect provider-specific constraints at the boundary.

**Independent Test**: Can be fully tested by exercising provider execution with invalid or unsupported queries and confirming execution still rejects them without relying on callers to preflight.

**Acceptance Scenarios**:

1. **Given** callers skip validation, **When** an unsupported query reaches a provider, **Then** the provider still rejects it explicitly.
2. **Given** validation behavior is shared across providers where appropriate, **When** a provider has additional execution constraints, **Then** those constraints remain enforced by execution.
3. **Given** provider capabilities and execution behavior diverge during development, **When** tests compare validation and execution, **Then** the mismatch is detected.

---

### User Story 4 - Document The Recommended Query Flow (Priority: P3)

As an SDK consumer, I want clear guidance for capability discovery, validation, and execution, so that I know when to preflight queries and how to handle unsupported provider behavior.

**Why this priority**: The feature improves safety only if consumers can understand the intended flow and the distinction between advisory validation and authoritative execution.

**Independent Test**: Can be fully tested by following the documentation to inspect capabilities, validate a query, handle failures, and execute a supported query.

**Acceptance Scenarios**:

1. **Given** a developer reads the querying documentation, **When** they follow the recommended flow, **Then** they can inspect capabilities before executing a query.
2. **Given** validation reports failures, **When** the developer handles those failures, **Then** they can avoid executing unsupported query shapes.
3. **Given** validation is skipped, **When** execution fails, **Then** documentation explains that providers still reject unsupported shapes.

### Edge Cases

- A custom provider replaces the active query service but does not declare capabilities.
- Multiple providers and capability declarations are registered during host setup.
- A query validates successfully before host configuration changes, then executes against a different active provider.
- A provider supports most of a query but rejects one provider-specific value shape during execution.
- A query contains multiple unsupported features; validation can report all detectable failures while execution may stop at the first blocking failure.
- Provider capability declarations drift from execution behavior.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is to align validation and execution feedback around the current portable query model. A query planner, automatic query rewriting, cross-provider negotiation, and new provider framework are intentionally out of scope.
- **Explicitness**: Active provider capabilities and failure categories must remain discoverable through registration, validation results, and execution errors.
- **Dependencies**: None expected.
- **Operational Impact**: No new infrastructure or deployment requirements. Debugging should improve because unsupported-query failures become more consistent between validation and execution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Query execution MUST reject unsupported provider query shapes explicitly even when callers skip preflight validation.
- **FR-002**: Query execution failures for unsupported query shapes MUST include a stable failure code, an unsupported feature category, and an actionable message.
- **FR-003**: Preflight validation and provider execution SHOULD use the same unsupported-feature categories where both paths can detect the same query shape.
- **FR-004**: Validation MUST fail closed with capabilities-not-declared when the active query provider has no matching capability declaration.
- **FR-005**: Validation MUST choose the active provider's capability declaration by explicit provider key when multiple providers are registered.
- **FR-006**: Registering a new query provider SHOULD require an explicit matching provider key in its capability declaration for validation to succeed.
- **FR-007**: Providers SHOULD run shared validation before execution and MUST keep provider-specific checks authoritative during translation and execution.
- **FR-008**: Provider tests MUST detect mismatches where validation accepts a query shape that execution rejects as unsupported.
- **FR-009**: Provider tests MUST detect mismatches where validation rejects a query shape that execution supports.
- **FR-010**: Validation MUST continue to report all detectable unsupported query features where practical.
- **FR-011**: Execution MAY stop at the first blocking unsupported feature, but the failure MUST expose the same stable code/category/message shape used for unsupported-query handling.
- **FR-012**: Existing supported queries MUST continue to execute successfully without requiring callers to invoke validation first.
- **FR-013**: Documentation MUST explain the recommended flow: inspect capabilities, validate when handling user-defined queries, then execute supported queries.
- **FR-014**: Documentation MUST explain that validation is advisory and execution remains authoritative.
- **FR-015**: The feature MUST NOT introduce public raw SQL, public `IQueryable<Resource>`, or provider-specific query construction as the execution contract.

### Key Entities *(include if feature involves data)*

- **Active Query Provider**: The provider currently registered to execute resource queries for the host application, identified for validation purposes by an explicit provider key.
- **Provider Capability Declaration**: The active provider's machine-readable description of supported query scopes, filters, comparisons, sorting, paging, and value shapes, identified by the same explicit provider key as the provider it describes.
- **Query Validation Failure**: A structured preflight failure identifying an unsupported query feature before execution.
- **Unsupported Query Execution Failure**: The execution-time failure raised when a provider receives a query shape it cannot execute; it includes a stable failure code, feature category, and actionable message.
- **Provider Consistency Case**: A test scenario that compares validation behavior with execution behavior for the same query shape.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For all documented unsupported SQLite query shapes, validation and execution identify the same unsupported feature category in provider consistency tests.
- **SC-002**: Custom query provider registration without matching capabilities produces a capabilities-not-declared validation result in 100% of covered configurations.
- **SC-003**: All existing supported in-memory and SQLite query execution tests continue to pass without requiring explicit preflight validation.
- **SC-004**: Provider consistency tests cover at least one supported query and at least three unsupported query shapes for each active provider included in the feature.
- **SC-005**: Documentation includes a complete capability discovery, validation, execution, and unsupported-failure handling example.

## Assumptions

- The active providers for this slice are the existing in-memory and SQLite JSON providers.
- Execution runs shared validation first but may still report one provider-specific blocking failure while validation may report multiple detectable failures.
- Capability declarations remain provider-owned and should not become a global query-planning framework.
- Provider matching uses an explicit provider key and does not require a new dependency.
