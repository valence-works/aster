# Feature Specification: Query Capabilities & Typed Query Helpers

**Feature Branch**: `003-query-capabilities-typed`  
**Created**: 2026-05-15  
**Status**: Draft  
**Input**: User description: "Add provider query capability discovery, query preflight validation, and typed query helper APIs that compile into the existing ResourceQuery AST while keeping ResourceQuery as the public execution contract and avoiding IQueryable exposure."

## Clarifications

### Session 2026-05-15

- Q: How should typed helpers determine aspect keys and facet identifiers? → A: Infer both aspect key and facet/member name from the aspect type by convention.
- Q: How should validation behave when the active provider has no declared capabilities? → A: Validation fails with “capabilities not declared”.
- Q: Should typed helper convention mapping allow overrides? → A: Convention by default, with explicit per-query override for aspect key and/or facet identifier.
- Q: What shape should query preflight validation use? → A: Validation returns a structured result with IsValid and a collection of failures.
- Q: What exact typed helper naming convention should be used? → A: Aspect key = typed aspect CLR type name; facet identifier = selected member name.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover Provider Query Support (Priority: P1)

As an application developer choosing a query shape, I want to know what the active query provider supports before I rely on it, so that unsupported filters, sorts, scopes, and comparisons are visible during development instead of surprising users at runtime.

**Why this priority**: Capability discovery is the safety rail for every later query improvement. Without it, typed helpers can produce query shapes that a provider cannot execute.

**Independent Test**: Can be fully tested by resolving the active query provider capabilities and confirming they describe the supported scopes, filter types, comparison operators, sort targets, facet range behavior, and unsupported features for both the in-memory and SQLite JSON providers.

**Acceptance Scenarios**:

1. **Given** an application configured with the default in-memory provider, **When** a developer inspects query capabilities, **Then** the system reports supported version scopes, metadata filtering, aspect presence filtering, facet value filtering, logical expressions, metadata sorting, and facet sorting as provider-supported where applicable.
2. **Given** an application configured with SQLite JSON persistence, **When** a developer inspects query capabilities, **Then** the system reports the SQLite-supported subset including metadata filters/sorts, version scopes, paging, aspect presence, scalar facet equality, string containment, and numeric facet ranges.
3. **Given** a provider does not support a query behavior, **When** capabilities are inspected, **Then** the unsupported behavior is discoverable without executing a query.

---

### User Story 2 - Validate Queries Before Execution (Priority: P1)

As an application developer, I want to validate a resource query against the active provider before execution, so that users get clear and actionable feedback when a query cannot be served by that provider.

**Why this priority**: Preflight validation turns provider limitations into explicit product behavior and prevents typed helpers from obscuring unsupported query shapes.

**Independent Test**: Can be fully tested by building supported and unsupported resource queries, validating them against each provider, and verifying success results for supported queries and actionable validation failures for unsupported queries.

**Acceptance Scenarios**:

1. **Given** a query that uses only provider-supported predicates, scopes, sorts, and paging, **When** the query is validated, **Then** validation succeeds without changing the query.
2. **Given** a SQLite-backed application and a query that requests facet sorting, **When** the query is validated, **Then** validation fails with a message identifying facet sorting as unsupported by the active provider.
3. **Given** a query with multiple unsupported features, **When** the query is validated, **Then** validation returns a structured result showing the query is invalid and listing all detectable unsupported features rather than stopping at the first one.
4. **Given** a query fails preflight validation, **When** the same query is executed through the standard query service, **Then** execution fails with the same category of actionable feedback rather than silently falling back to another provider.

---

### User Story 3 - Build Queries With Typed Helpers (Priority: P2)

As an application developer using typed aspects, I want typed query helpers that produce the existing portable query model, so that I can write less stringly-typed query construction while preserving the same provider-agnostic execution contract.

**Why this priority**: Typed helpers improve developer experience after capability discovery and validation establish safe boundaries.

**Independent Test**: Can be fully tested by constructing queries with typed helpers, verifying the produced portable query model, and executing the produced query against supported providers.

**Acceptance Scenarios**:

1. **Given** a typed aspect with scalar properties, **When** a developer builds an equality or contains predicate through typed helpers, **Then** the helper produces an equivalent portable query using convention-derived aspect key and facet identifier values.
2. **Given** a typed numeric facet, **When** a developer builds a range predicate through typed helpers, **Then** the helper produces an equivalent portable range query.
3. **Given** a typed helper needs to target a named aspect or non-conventional facet identifier, **When** the developer provides a per-query override, **Then** the produced portable query uses the override values and remains inspectable.
4. **Given** a typed helper produces a query unsupported by the active provider, **When** the query is validated or executed, **Then** the same provider capability failure is reported.
5. **Given** a developer uses typed helpers, **When** the query is executed, **Then** execution still uses the portable resource query contract rather than a public provider-specific query abstraction.

---

### User Story 4 - Preserve Provider-Agnostic Query Semantics (Priority: P3)

As a library maintainer, I want typed helpers and capability checks to reinforce the existing query architecture, so that Aster remains portable across in-memory, SQLite, and future providers.

**Why this priority**: This protects the architecture while the developer-facing query experience becomes more ergonomic.

**Independent Test**: Can be fully tested by confirming typed helpers output the portable query model, no public queryable provider contract is introduced, and provider-specific differences remain declared through capabilities and validation.

**Acceptance Scenarios**:

1. **Given** a query built with typed helpers, **When** the application inspects it, **Then** it is represented as the same portable query model used by manually built queries.
2. **Given** a provider-specific limitation, **When** developers inspect capabilities or validation failures, **Then** the limitation is described explicitly rather than hidden behind conventions.
3. **Given** future providers are added, **When** they expose their capabilities, **Then** they can participate in validation without changing the portable query model.

---

### Edge Cases

- A query contains nested logical expressions with both supported and unsupported child predicates.
- A query requests a supported filter over an unsupported metadata or facet field shape.
- A query requests an unsupported sort while all predicates are supported.
- A typed helper references a property that cannot be mapped to a queryable facet.
- A provider has no declared capabilities; validation fails closed with an actionable "capabilities not declared" result.
- A provider supports a feature only for specific value shapes, such as numeric ranges but not date-like ranges.
- A query validates successfully before provider configuration changes, then is executed after the active provider changes.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is capability discovery, validation, and typed helper output over the existing portable query model. A full query planner, public queryable provider, advanced indexing engine, and cross-provider optimization are intentionally out of scope.
- **Explicitness**: Provider support must be visible through declared capabilities and validation results. Typed helper conventions must be documented and the generated portable query must remain inspectable rather than hidden behind provider-specific behavior.
- **Dependencies**: None expected. The feature should use existing platform and project capabilities unless a later plan identifies a concrete need.
- **Operational Impact**: No new service infrastructure should be required. Local development and debugging should remain straightforward: developers can inspect capabilities, validate queries, and see actionable failure messages.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a query capability description for the active query provider.
- **FR-002**: Query capabilities MUST describe supported version scopes, filter expression types, comparison operators, logical operators, sort categories, paging support, and provider-specific exclusions.
- **FR-003**: Query capabilities MUST distinguish metadata sorting from facet sorting.
- **FR-004**: Query capabilities MUST distinguish scalar facet equality, string containment, and numeric range support from unsupported range value shapes.
- **FR-005**: The system MUST provide a way to validate a resource query against provider capabilities before execution.
- **FR-006**: Query validation MUST return success for supported queries without mutating the query being validated.
- **FR-007**: Query validation MUST return a structured result containing an overall validity indicator and a collection of validation failures.
- **FR-008**: Query validation MUST return actionable failures for unsupported scopes, predicates, comparison operators, sorts, paging values, and value shapes.
- **FR-009**: Query validation MUST report all detectable unsupported features in a query where practical.
- **FR-010**: Query validation MUST fail closed with an actionable "capabilities not declared" result when the active provider has no capability description.
- **FR-011**: Query execution MUST continue to reject unsupported provider query shapes explicitly; validation must not introduce silent in-memory fallback behavior.
- **FR-012**: The in-memory provider MUST declare capabilities matching its currently supported query behavior.
- **FR-013**: The SQLite JSON provider MUST declare capabilities matching its currently supported query behavior.
- **FR-014**: Typed query helpers MUST produce the existing portable resource query model rather than a provider-specific execution contract.
- **FR-015**: Typed query helpers MUST support typed construction of aspect presence, facet equality, string containment, and numeric range predicates for convention-mapped typed aspects.
- **FR-016**: Typed query helpers MUST infer both the aspect key and facet identifier from the typed aspect and selected member by documented convention.
- **FR-017**: Typed query helpers MUST allow explicit per-query overrides for aspect key and/or facet identifier.
- **FR-018**: Typed query helpers MUST keep generated aspect keys and facet identifiers visible in the produced portable query so generated queries are inspectable and debuggable.
- **FR-019**: Typed query helpers MUST surface a clear failure when a typed member cannot be mapped to a queryable facet by convention or explicit override.
- **FR-020**: The public query execution contract MUST remain the portable resource query model; the feature MUST NOT expose a public queryable resource provider contract.
- **FR-021**: Documentation MUST explain provider capability discovery, validation behavior, typed helper output, current provider differences, typed mapping conventions, and per-query override behavior.

### Key Entities *(include if feature involves data)*

- **Query Capability Description**: Declares what a provider can execute, including scopes, filters, comparisons, sorting, paging, and known exclusions.
- **Query Validation Result**: Represents whether a query is supported by a provider, including an overall validity indicator and, when unsupported, a collection of actionable failures.
- **Query Validation Failure**: A single unsupported query feature with a location or category, a human-readable explanation, and the affected query shape.
- **Typed Query Mapping**: The convention-derived or per-query overridden association between a typed aspect type/member and the aspect key plus facet identifier used in the portable query model. By default, the aspect key is the typed aspect CLR type name and the facet identifier is the selected member name.
- **Typed Query Helper Output**: A portable resource query or filter expression produced from typed aspect-oriented construction.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can determine whether a provider supports a given query shape before execution in 100% of supported-provider configurations.
- **SC-002**: Validation reports actionable structured failures for unsupported query shapes in at least 95% of detectable cases covered by the current query model.
- **SC-003**: Typed helpers reduce manually typed aspect/facet identifier usage for common typed aspect queries by at least 50% in documented examples.
- **SC-004**: All typed helper-generated queries can be inspected as the portable query model before execution.
- **SC-005**: No public queryable resource provider contract is introduced.
- **SC-006**: Existing manually built resource queries continue to behave as before unless validation is explicitly requested or execution already rejects an unsupported shape.

## Assumptions

- The active providers for this feature are the existing in-memory and SQLite JSON providers.
- Capability discovery describes the provider's current behavior; it does not promise advanced indexing, full-text search, or future provider optimizations.
- Typed helpers target common scalar typed aspect members first.
- Typed helper naming conventions are part of the user-visible contract: by default, aspect key equals the typed aspect CLR type name and facet identifier equals the selected member name.
- Provider capability validation is advisory before execution but execution must continue to enforce unsupported behavior.
- Advanced indexing, provider capability negotiation across multiple simultaneous providers, and versioned definition schema upgrades are future work.
