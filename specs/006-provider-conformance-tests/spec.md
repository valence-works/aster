# Feature Specification: Provider Conformance Tests

**Feature Branch**: `006-provider-conformance-tests`  
**Created**: 2026-05-17  
**Status**: Draft  
**Input**: User description: "Add provider conformance tests so query provider authors can verify that an implementation matches its declared query capabilities and expected validation/execution behavior."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify A Provider Matches Its Capabilities (Priority: P1)

As a query provider author, I want a reusable conformance test suite that checks whether my provider accepts the query shapes it declares as supported and rejects the query shapes it declares as unsupported, so I can catch mismatches before shipping the provider.

**Why this priority**: Provider capability declarations are now central to fail-closed validation. The most important value is proving those declarations are truthful.

**Independent Test**: Can be fully tested by running the conformance suite against a sample provider with known supported and unsupported capabilities and confirming that mismatches are reported as test failures.

**Acceptance Scenarios**:

1. **Given** a provider declares support for a query shape, **When** the conformance suite runs the matching positive case, **Then** the provider completes the query without an unsupported-feature failure.
2. **Given** a provider does not declare support for a query shape, **When** the conformance suite runs the matching negative case, **Then** validation or execution returns a structured unsupported-feature failure rather than silently accepting the shape.
3. **Given** a provider declaration and execution behavior disagree, **When** the conformance suite runs, **Then** the mismatch is visible as a focused failing test with enough context to identify the capability area.

---

### User Story 2 - Reuse The Same Checks Across Built-In And Custom Providers (Priority: P2)

As a maintainer, I want the same conformance expectations to exercise both built-in providers and custom provider examples, so regressions in provider behavior are caught consistently.

**Why this priority**: A shared test suite is most valuable when it reduces duplicated provider-specific test logic and keeps built-in behavior aligned with public provider-authoring guidance.

**Independent Test**: Can be tested by running the conformance suite against the in-memory provider, the SQLite JSON provider, and a minimal custom provider fixture.

**Acceptance Scenarios**:

1. **Given** an in-memory provider registration, **When** the conformance suite runs, **Then** the provider passes the checks matching its declared capabilities.
2. **Given** a SQLite JSON provider registration, **When** the conformance suite runs, **Then** the provider passes supported checks and rejects unsupported checks matching its declared capabilities.
3. **Given** a minimal custom provider fixture, **When** the conformance suite runs, **Then** provider authors can see the minimum setup required to reuse the suite.

---

### User Story 3 - Diagnose Provider Authoring Mistakes Quickly (Priority: P3)

As a provider author, I want conformance failures to identify whether the issue is missing capabilities, mismatched provider keys, unsupported query handling, or incorrect accepted behavior, so I can fix the right layer without reverse-engineering test internals.

**Why this priority**: Diagnostics make the suite useful beyond pass/fail, but they build on the core conformance checks.

**Independent Test**: Can be tested by running the suite against intentionally broken fixtures and verifying that each failure points to the relevant provider-authoring mistake.

**Acceptance Scenarios**:

1. **Given** a provider has no matching capability declaration, **When** the conformance suite runs, **Then** the failure identifies missing or mismatched capabilities.
2. **Given** a provider accepts a query shape that its capabilities reject, **When** the conformance suite runs, **Then** the failure identifies the accepted unsupported query area.
3. **Given** a provider rejects a query shape that its capabilities allow, **When** the conformance suite runs, **Then** the failure identifies the rejected supported query area.

### Edge Cases

- A provider has a missing, empty, or mismatched provider key.
- A provider advertises support for a query area but has no fixture data capable of proving the behavior.
- A provider correctly rejects unsupported shapes during validation rather than execution.
- A provider returns no results for a supported query because fixture data was not arranged correctly.
- A provider supports a broad capability area but only supports a narrower value shape within that area.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature SHOULD provide a small reusable conformance harness and focused fixtures. It MUST NOT introduce a provider registry, query planner, runtime scanning, public raw SQL contract, or public queryable API.
- **Explicitness**: Provider authors SHOULD opt in by explicitly supplying provider setup, capability declarations, fixture data, and expected query cases. Behavior MUST be discoverable from test inputs and failure output.
- **Dependencies**: None. The feature SHOULD use the existing test stack and platform capabilities.
- **Operational Impact**: The suite SHOULD run as ordinary tests with no additional services. SQLite-backed checks SHOULD use local disposable storage and leave no durable artifacts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a reusable conformance test surface that can be applied to any active query provider with a matching capability declaration.
- **FR-002**: The conformance suite MUST verify that supported query shapes declared by provider capabilities are accepted by the provider.
- **FR-003**: The conformance suite MUST verify that unsupported query shapes are rejected with structured unsupported-feature behavior.
- **FR-004**: The conformance suite MUST verify fail-closed behavior when the active provider has missing, empty, or mismatched capability declarations.
- **FR-005**: Provider authors MUST be able to supply explicit provider setup and fixture data without relying on runtime scanning or automatic discovery.
- **FR-006**: The suite MUST include built-in coverage for the in-memory provider.
- **FR-007**: The suite MUST include built-in coverage for the SQLite JSON provider.
- **FR-008**: The suite MUST include at least one minimal custom-provider fixture demonstrating how custom providers opt in.
- **FR-009**: Conformance failures SHOULD identify the capability area under test and whether the provider unexpectedly accepted or rejected the query.
- **FR-010**: The suite MUST preserve existing provider-specific tests; it SHOULD reduce duplication only where a shared expectation is demonstrated.
- **FR-011**: The feature MUST document how maintainers and custom provider authors run or adapt the conformance checks.
- **FR-012**: The feature MUST NOT add new runtime dependencies, storage formats, query-planning infrastructure, raw SQL APIs, or public queryable APIs.

### Key Entities

- **Provider Conformance Subject**: A provider-under-test, its provider key, matching capability declaration, setup routine, fixture data, and query execution entry point.
- **Conformance Query Case**: A named query shape with expected supported or unsupported behavior and a capability area label.
- **Conformance Failure**: A focused test failure describing the provider, query case, capability area, and observed behavior.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Built-in in-memory and SQLite JSON providers pass the shared conformance suite in the normal solution test run.
- **SC-002**: At least one intentionally broken provider fixture produces a focused conformance failure for each major mistake category: missing capabilities, accepted unsupported shape, and rejected supported shape.
- **SC-003**: A maintainer can add a new provider conformance subject by editing one explicit setup location and adding provider fixture data, without changing shared conformance logic.
- **SC-004**: The full solution test run remains self-contained and requires no external services or durable local setup.

## Assumptions

- Conformance checks will focus on the existing portable `ResourceQuery` behavior and declared capability model.
- Providers remain authoritative at execution time; validation is a shared preflight, not a substitute for provider execution checks.
- Fixture data may be provider-specific, but conformance expectations should remain shared wherever the declared capability surface is shared.
