# Feature Specification: Explicit Indexing Model

**Feature Branch**: `011-explicit-indexing-model`  
**Created**: 2026-05-18  
**Status**: Draft  
**Input**: User description: "Introduce an explicit indexing model as a provider-declared capability. Define index field types such as Keyword, Text, NormalizedText, Boolean, Integer, Decimal, DateTime, Guid, and KeywordArray; add provider-facing contracts for declaring and consuming index projections; keep SQLite JSON simple and defer heavy query planning, runtime scanning, automatic discovery, public SQL, and IQueryable."

## Clarifications

### Session 2026-05-18

- Q: Where are index projections declared for this slice? → A: Index projections are declared by providers/capability providers only; resource definitions are not changed in this slice.
- Q: Should built-in providers declare default index projections in this slice? → A: Built-in providers declare zero default projections; tests use custom/test provider declarations to prove the model works.
- Q: Which projection source shapes are in scope? → A: Projection sources are limited to metadata fields and aspect/facet pairs; nested paths and provider-specific paths are out of scope.
- Q: How should projection evaluation report incompatible values? → A: Evaluation returns successful projection values plus structured per-projection failures.
- Q: Should projection evaluation coerce values between shapes? → A: Projection evaluation uses strict shape matching; only existing accepted DateTime normalization is allowed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Declare Queryable Index Projections (Priority: P1)

As a provider author or host integrator, I want to declare the fields a provider can index from resource metadata and facets through provider capabilities, so query behavior is explicit before resources are queried or stored.

**Why this priority**: This is the smallest useful indexing slice. It establishes an intentional contract without changing query semantics or introducing a planner.

**Independent Test**: Can be tested by declaring index projections with supported field types and verifying that callers can inspect the provider's declared index model.

**Acceptance Scenarios**:

1. **Given** a provider that supports indexed projections, **When** it declares metadata and facet projections, **Then** the provider exposes each projection with a stable name, metadata field or aspect/facet source, field type, and multiplicity.
2. **Given** an index projection declaration, **When** a field uses `Keyword`, `Text`, `NormalizedText`, `Boolean`, `Integer`, `Decimal`, `DateTime`, `Guid`, or `KeywordArray`, **Then** the declaration is considered valid for provider capability reporting.
3. **Given** a projection declaration with an unsupported or ambiguous field type, **When** capabilities are validated or consumed, **Then** the system reports a structured failure instead of silently accepting it.

---

### User Story 2 - Keep Provider Capability Discovery Honest (Priority: P2)

As an application developer, I want query capabilities to state which indexed projections exist, so I can understand what a provider is prepared to optimize or expose without relying on hidden discovery.

**Why this priority**: The existing query capability model is the right place to make indexing discoverable. This keeps provider behavior explicit and avoids runtime scanning.

**Independent Test**: Can be tested by comparing provider capability descriptions and confirming that index projection capabilities are visible, provider-specific, and absent when a provider does not declare them.

**Acceptance Scenarios**:

1. **Given** the built-in in-memory provider, **When** its capabilities are inspected, **Then** it reports zero default index projections while still supporting existing query behavior.
2. **Given** the SQLite JSON provider, **When** its capabilities are inspected, **Then** it reports zero default index projections and does not imply automatic indexing for every facet.
3. **Given** a custom provider, **When** it registers capabilities, **Then** it can include index projection declarations without using a global registry, runtime scanning, or automatic discovery.

---

### User Story 3 - Consume Projection Values Consistently (Priority: P3)

As a provider implementer, I want a small provider-facing contract for evaluating declared projections against a resource version, so indexable values are shaped consistently across providers.

**Why this priority**: Provider authors need a clear way to consume declarations, but the first slice should stop short of building a storage engine or query planner.

**Independent Test**: Can be tested by applying projection declarations to representative resource versions and verifying the resulting values are typed, repeatable, and fail predictably for missing or incompatible source values.

**Acceptance Scenarios**:

1. **Given** a resource version with matching metadata and facet values, **When** declared projections are evaluated, **Then** the resulting values match the declared field types.
2. **Given** a resource version missing an optional projected source value, **When** projections are evaluated, **Then** the missing value is represented consistently without failing the whole projection set.
3. **Given** a resource version with a value incompatible with a declared projection type, **When** projections are evaluated, **Then** the result includes any successful projection values plus a structured failure identifying the incompatible projection and reason.

### Edge Cases

- A declaration references a facet path that is absent on a resource version.
- A declaration attempts to use a nested facet path or provider-specific source path.
- A declaration references a repeated or array value but the declared field type is scalar.
- A declaration uses `KeywordArray` for a scalar value.
- A date/time projection receives a date-only string or an unsupported date/time shape.
- A scalar projection receives a string that looks like a number, boolean, or GUID but is not already in the matching value shape.
- Two projections declare the same stable index field name.
- One projection fails while other projections on the same resource version are valid.
- A provider declares index projections while continuing to support queries that are not backed by physical indexes.
- A custom provider declares no index model.
- An application resource definition has no index projection annotations because resource definitions are not changed in this slice.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The slice defines explicit declarations and projection consumption only. It does not add a query planner, migration engine, generated columns, background indexer, runtime scanning, or automatic discovery.
- **Explicitness**: Indexing behavior is visible through provider capability declarations and explicit projection definitions. Providers must opt in by declaring what they support.
- **Dependencies**: None.
- **Operational Impact**: Local development remains ordinary build and test execution. SQLite JSON does not require a schema migration or operational index maintenance in this slice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define an index field type model that includes `Keyword`, `Text`, `NormalizedText`, `Boolean`, `Integer`, `Decimal`, `DateTime`, `Guid`, and `KeywordArray`.
- **FR-002**: The system MUST allow providers or provider capability declarations to declare explicit index projections with a stable field name, source description, field type, and scalar-or-multi-value behavior.
- **FR-002a**: Index projection sources MUST be limited to resource metadata fields or aspect/facet pairs.
- **FR-003**: The system MUST expose provider-declared index projections through provider capability discovery.
- **FR-004**: The system MUST preserve existing query validation and execution behavior when a provider declares no index projections.
- **FR-005**: The system MUST reject or report duplicate projection field names within the same provider declaration.
- **FR-006**: The system MUST report structured diagnostics when an index projection declaration is invalid.
- **FR-007**: The system MUST provide a provider-facing way to evaluate declared projections against a resource version and receive typed projection values.
- **FR-008**: Projection evaluation MUST distinguish missing source values from incompatible source values.
- **FR-009**: Projection evaluation MUST preserve the existing accepted date/time value rules for `DateTime` projections.
- **FR-010**: The SQLite JSON provider MUST NOT imply automatic physical indexes for all metadata or facet values.
- **FR-011**: The feature MUST NOT introduce runtime scanning, automatic discovery, a provider registry, a query planner, public raw SQL, or public `IQueryable<Resource>`.
- **FR-012**: Existing provider registration, provider capabilities, query validation, query execution, and provider conformance behavior MUST remain compatible.
- **FR-013**: Documentation MUST explain how provider authors declare projections, how applications inspect them, and what remains out of scope for physical indexing.
- **FR-014**: The feature MUST NOT add index projection declarations to resource definitions in this slice.
- **FR-015**: Built-in in-memory and SQLite JSON providers MUST declare zero default index projections in this slice.
- **FR-016**: The feature MUST NOT introduce nested facet path syntax, JSONPath-style source syntax, or provider-specific source path strings.
- **FR-017**: Projection evaluation MUST return successful projection values and structured per-projection failures together, without failing the whole evaluation when one projected value is incompatible.
- **FR-018**: Projection evaluation MUST use strict shape matching for projected values and MUST NOT coerce strings into numeric, boolean, or GUID values.
- **FR-019**: `DateTime` projection evaluation MAY normalize values according to the existing accepted date/time value rules.

### Key Entities

- **Index Field Type**: The declared shape of an indexed value, such as keyword text, normalized text, numeric values, date/time values, GUIDs, booleans, and keyword arrays.
- **Index Projection**: A provider-declared mapping from a resource metadata field or an aspect/facet pair to an index field name and field type.
- **Index Model Capability**: The provider capability section that communicates which index projections a provider declares.
- **Projection Evaluation Result**: The typed values and structured per-projection failures produced when declarations are applied to a resource version using strict value-shape matching.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Provider authors can declare at least one metadata projection and one facet projection using the index model without adding any new third-party dependency.
- **SC-002**: Capability inspection clearly shows that built-in providers declare zero index projections by default and that custom/test providers can declare one or multiple projections.
- **SC-003**: Projection evaluation returns valid values and structured per-projection failures together while distinguishing valid values, missing values, and incompatible values in independently testable outcomes.
- **SC-004**: Existing provider capability, validation, execution, and conformance tests continue to pass unchanged except where new index-model assertions are intentionally added.
- **SC-005**: Documentation enables a provider author to understand the minimal declaration path and the explicit out-of-scope boundaries in under 10 minutes.
