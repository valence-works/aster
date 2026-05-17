# Feature Specification: Provider Authoring Ergonomics

**Feature Branch**: `005-provider-authoring-ergonomics`  
**Created**: 2026-05-16  
**Status**: Draft  
**Input**: User description: "Provider authoring ergonomics: make custom query provider registration easier and harder to misconfigure after explicit provider keys and fail-closed validation. Add clear guidance and small SDK affordances so provider authors can declare identity, capabilities, and active query service wiring consistently without introducing a provider registry framework."

## Clarifications

### Session 2026-05-17

- Q: Should the new provider registration helper support configurable lifetimes or remain singleton-only? → A: `AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>()` registers both concrete implementations and shared interfaces as singletons; other lifetimes use manual registration.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register A Custom Query Provider Correctly (Priority: P1)

As a provider author, I want a clear and repeatable way to register a custom query provider and its matching capabilities, so that validation uses my provider instead of failing closed or accidentally relying on defaults.

**Why this priority**: Provider extensibility is only useful if custom providers can be wired correctly without studying the built-in providers.

**Independent Test**: Can be fully tested by registering a custom provider with a matching provider key and confirming capability discovery, validation, and execution all use the custom provider.

**Acceptance Scenarios**:

1. **Given** a custom query provider with a declared provider key and matching capabilities, **When** the host registers it as the active query provider, **Then** validation succeeds for query shapes declared by that provider.
2. **Given** default in-memory services are already registered, **When** a custom query provider is registered afterward with matching capabilities, **Then** validation uses the custom provider capabilities instead of earlier defaults.
3. **Given** a host resolves provider capabilities after custom registration, **When** capabilities are inspected, **Then** the active custom provider identity and supported query surface are discoverable.

---

### User Story 2 - Catch Provider Registration Mistakes Early (Priority: P1)

As a host developer, I want provider registration mistakes to be obvious during local development and tests, so that missing or mismatched capability declarations do not survive until production query execution.

**Why this priority**: Fail-closed validation is safe, but authors still need fast feedback that explains how to fix the registration.

**Independent Test**: Can be fully tested by intentionally registering incomplete or mismatched custom provider pieces and confirming validation reports actionable failures.

**Acceptance Scenarios**:

1. **Given** a custom query provider is registered without matching capabilities, **When** a query is validated, **Then** validation fails with a capabilities-not-declared result that identifies missing active-provider capabilities.
2. **Given** a custom provider key and capability provider key do not match, **When** validation runs, **Then** validation fails closed and the failure message explains that provider identity and capabilities must match.
3. **Given** multiple capability declarations are registered, **When** validation runs for the active provider, **Then** the active provider's declaration is selected deterministically by explicit key.

---

### User Story 3 - Reuse Validation In Custom Provider Execution (Priority: P2)

As a provider author, I want guidance and reusable patterns for invoking shared validation before execution, so that my provider reports unsupported query shapes consistently while still enforcing provider-specific constraints.

**Why this priority**: Consistent failure behavior reduces duplicated validation logic, but provider execution must remain authoritative.

**Independent Test**: Can be fully tested by implementing a small custom provider test double that runs shared validation before execution and still rejects a provider-specific unsupported value shape.

**Acceptance Scenarios**:

1. **Given** a custom provider receives an unsupported query shape, **When** execution runs without caller preflight, **Then** execution raises a structured unsupported-query failure aligned with validation.
2. **Given** a custom provider has an execution-only constraint, **When** validation accepts the query but execution cannot complete it, **Then** execution raises a structured provider-specific unsupported-query failure.
3. **Given** a supported query shape reaches the custom provider, **When** execution runs, **Then** shared validation does not block execution.

---

### User Story 4 - Follow Provider Authoring Documentation (Priority: P3)

As an SDK consumer evaluating provider extensibility, I want concise provider-authoring documentation and examples, so that I can understand the minimum required pieces without reverse-engineering built-in providers.

**Why this priority**: Documentation turns the new provider identity and fail-closed behavior into an approachable extension story.

**Independent Test**: Can be fully tested by following the documentation to create a minimal custom provider, declare capabilities, register it, validate a query, and handle execution failures.

**Acceptance Scenarios**:

1. **Given** a developer reads the provider authoring guide, **When** they follow the minimal example, **Then** they can identify the required provider identity, capability declaration, query service, and registration steps.
2. **Given** validation fails because capabilities are missing or mismatched, **When** the developer reads troubleshooting guidance, **Then** they can identify the registration problem and fix it.
3. **Given** a provider has unsupported query behavior, **When** the developer follows the guide, **Then** they can expose structured unsupported-query failures with stable code, feature, message, and optional path.

### Edge Cases

- A host registers a custom query provider but forgets to register matching capabilities.
- A host registers matching capabilities before the custom provider becomes active.
- A host registers multiple capability declarations with overlapping provider names but distinct provider keys.
- A custom provider wraps or decorates another provider while exposing its own provider key.
- A provider declares support for a query shape but rejects a provider-specific value during execution.
- A provider author tries to solve registration by introducing a global provider registry instead of explicit registration.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is small provider-authoring affordances, tests, and documentation around the existing provider identity/capability model. A global provider registry, provider framework, query planner, automatic provider discovery, and runtime scanning are intentionally out of scope.
- **Explicitness**: Provider identity, capability declarations, validation matching, and registration order must remain visible through code and configuration.
- **Dependencies**: None expected.
- **Operational Impact**: No new infrastructure or deployment requirements. Local development and debugging should improve because provider registration mistakes become easier to diagnose.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The SDK MUST provide a clear supported path for registering a custom query provider and matching capability declaration.
- **FR-002**: Provider registration guidance MUST make the active provider key and capability provider key relationship explicit.
- **FR-003**: Validation MUST continue to fail closed when the active query provider has no matching capability declaration.
- **FR-004**: Validation failure messages for missing or mismatched active-provider capabilities SHOULD be actionable for provider authors.
- **FR-005**: Custom provider registration tests MUST prove that a matching provider key causes validation to use custom capabilities instead of defaults.
- **FR-006**: Custom provider registration tests MUST prove that missing or mismatched capabilities fail closed.
- **FR-007**: Provider authoring guidance SHOULD show how a provider can run shared validation before execution without making validation the only execution safeguard.
- **FR-008**: Provider authoring guidance MUST explain that provider execution remains authoritative and may still reject provider-specific constraints.
- **FR-009**: Structured unsupported-query failure guidance MUST include stable code, feature category, actionable message, and optional path handling.
- **FR-010**: The feature MUST NOT introduce a global provider registry, runtime scanning convention, public raw SQL, public `IQueryable<Resource>`, or provider-specific query construction as the custom provider contract.
- **FR-011**: Existing built-in in-memory and SQLite JSON provider registration behavior MUST remain compatible.
- **FR-012**: Documentation MUST include a minimal custom provider example and a troubleshooting section for missing or mismatched capabilities.
- **FR-013**: The custom provider registration helper MUST register the query service and capabilities provider as singletons for both concrete types and shared interfaces; hosts that require different lifetimes MUST use explicit manual DI registration.

### Key Entities *(include if feature involves data)*

- **Custom Query Provider**: A host-provided implementation that executes portable resource queries and exposes a stable provider key.
- **Custom Capability Declaration**: A provider-owned declaration of supported query shapes using the same provider key as the active custom provider.
- **Provider Registration Recipe**: The minimum host configuration needed to make the custom provider active for execution and validation. The SDK helper recipe uses singleton registrations; manual registration remains the supported recipe for alternate lifetimes.
- **Provider Authoring Failure**: A validation or execution failure that helps provider authors diagnose missing capabilities, mismatched keys, or unsupported provider-specific behavior.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A minimal custom provider can be registered in tests and validated successfully with matching capabilities.
- **SC-002**: Missing or mismatched custom provider capabilities produce capabilities-not-declared validation results in all covered configurations.
- **SC-003**: Documentation includes a complete minimal custom provider registration example, including provider identity, capabilities, query service registration, validation, and execution failure handling.
- **SC-004**: Existing built-in provider registration and query validation tests continue to pass.
- **SC-005**: No new third-party dependencies, runtime scanning mechanisms, or provider registry abstractions are introduced.

## Assumptions

- This slice builds on explicit provider keys and structured unsupported-query failures from `004-provider-validation-execution`.
- The primary provider-authoring target is query provider registration and validation behavior, not full persistence provider packaging.
- Small SDK affordances are acceptable when they remove current duplication or prevent demonstrated misregistration, but broad extension frameworks are out of scope.
