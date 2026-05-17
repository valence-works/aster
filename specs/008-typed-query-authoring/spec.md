# Feature Specification: Typed Query Authoring Ergonomics

**Feature Branch**: `008-typed-query-authoring`  
**Created**: 2026-05-17  
**Status**: Draft  
**Input**: User description: "Add typed query authoring ergonomics so callers can build facet sorts and simple logical filters from typed aspect members without stringly typed SortExpression and LogicalExpression construction, while preserving the existing ResourceQuery AST and avoiding LINQ/IQueryable."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build Typed Facet Sorts (Priority: P1)

As an SDK user, I want to build facet sort expressions from typed aspect members so I do not need to repeat aspect keys and facet identifiers as raw strings when sorting query results.

**Why this priority**: Facet sorting is now supported by both in-memory and SQLite JSON providers, but callers still need manual `SortExpression` construction for typed aspects.

**Independent Test**: Can be tested by creating a typed sort from an aspect member and asserting that it produces the same portable sort AST as the equivalent manual expression.

**Acceptance Scenarios**:

1. **Given** a typed aspect with a readable member, **When** a caller builds an ascending sort for that member, **Then** the result is a `SortExpression` with the resolved aspect key, facet identifier, and ascending direction.
2. **Given** a typed aspect with a readable member, **When** a caller builds a descending sort for that member, **Then** the result is a `SortExpression` with descending direction and the same convention-based identifiers.
3. **Given** a caller supplies an aspect key or facet identifier override, **When** a typed sort is created, **Then** the override is reflected in the resulting `SortExpression`.

---

### User Story 2 - Compose Common Logical Filters (Priority: P2)

As an SDK user, I want small typed-query helpers for common logical combinations so I can compose filters without manually constructing `LogicalExpression` records for simple `And`, `Or`, and `Not` cases.

**Why this priority**: Logical filters are already part of the public AST, but authoring them directly is noisy and easy to make invalid for `Not`.

**Independent Test**: Can be tested by composing existing typed or manual filters and asserting the resulting AST is equivalent to manual construction.

**Acceptance Scenarios**:

1. **Given** two or more filter expressions, **When** a caller composes them with `And`, **Then** the result is a logical `And` expression preserving operand order.
2. **Given** two or more filter expressions, **When** a caller composes them with `Or`, **Then** the result is a logical `Or` expression preserving operand order.
3. **Given** one filter expression, **When** a caller wraps it with `Not`, **Then** the result is a logical `Not` expression with exactly one operand.

---

### User Story 3 - Keep The Query AST Explicit (Priority: P3)

As a provider author, I want typed authoring helpers to emit the existing portable query records so provider validation, conformance tests, and execution behavior continue to work unchanged.

**Why this priority**: The helpers should reduce call-site friction without introducing a new query pipeline or provider-facing abstraction.

**Independent Test**: Can be tested by validating and executing helper-generated queries against existing providers and by asserting no public `IQueryable<Resource>` surface is introduced.

**Acceptance Scenarios**:

1. **Given** a helper-generated filter or sort, **When** it is passed to the existing validator, **Then** validation behaves the same as it does for the equivalent manually constructed AST.
2. **Given** a helper-generated query, **When** it executes against a provider that supports the equivalent manual query, **Then** execution returns the same results.
3. **Given** the public SDK surface, **When** abstractions are inspected, **Then** no public `IQueryable<Resource>` provider is exposed.

### Edge Cases

- Non-member selector expressions MUST fail with the same clear selector error used by existing typed facet filters.
- Static members, nested member access, method calls, and computed expressions MUST NOT be accepted as typed facet selectors.
- Empty logical compositions MUST fail clearly rather than producing provider-specific invalid queries.
- `Not` MUST require exactly one filter operand.
- Helpers MUST preserve manual construction as a supported advanced path.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature SHOULD add small helper methods over existing records, not a new query framework.
- **Explicitness**: Helper output MUST be ordinary `ResourceQuery`, `FilterExpression`, and `SortExpression` values that callers and provider authors can inspect.
- **Dependencies**: No new dependencies.
- **Operational Impact**: No storage, deployment, migration, or provider configuration changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The SDK MUST allow callers to create ascending and descending facet `SortExpression` values from typed aspect member selectors.
- **FR-002**: Typed sort helpers MUST use the same aspect key and facet identifier resolution rules as existing typed facet filter helpers.
- **FR-003**: Typed sort helpers MUST support per-query aspect key and facet identifier overrides.
- **FR-004**: The SDK SHOULD provide simple logical composition helpers for `And`, `Or`, and `Not` over existing `FilterExpression` values.
- **FR-005**: Logical composition helpers MUST reject empty `And`/`Or` operand sets, and `Not` MUST accept exactly one operand in its public API.
- **FR-006**: Helper-generated filters and sorts MUST validate and execute like equivalent manually constructed query AST values.
- **FR-007**: The feature MUST NOT introduce a LINQ provider, public `IQueryable<Resource>`, runtime scanning, provider registry, automatic discovery, raw SQL API, or query planner.
- **FR-008**: Existing manual `ResourceQuery`, `FilterExpression`, and `SortExpression` construction MUST remain supported.
- **FR-009**: Documentation MUST show typed facet filtering, typed facet sorting, and logical composition examples.

### Key Entities *(include if feature involves data)*

- **Typed facet selector**: A readable member expression used to resolve a facet identifier from a typed aspect.
- **Typed facet sort helper**: A helper that turns a typed facet selector into a `SortExpression`.
- **Logical composition helper**: A helper that turns one or more existing filters into a `LogicalExpression`.
- **Portable query AST**: The existing records consumed by validation and providers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Callers can build typed ascending and descending facet sorts without writing an aspect key or facet identifier string in the convention-based case.
- **SC-002**: Existing typed query helper tests cover filter helpers, sort helpers, selector rejection, overrides, and logical composition.
- **SC-003**: Helper-generated queries pass existing provider validation and execute successfully against at least one supported provider.
- **SC-004**: Public abstraction inspection confirms no public `IQueryable<Resource>` query provider is introduced.
- **SC-005**: Full solution tests pass after the helper additions.

## Assumptions

- The helper surface remains in the core SDK near the existing `TypedQuery` helpers.
- The feature is an authoring convenience only; provider semantics and capabilities remain unchanged.
- Typed sort helpers target facet sorting, not metadata sorting.
