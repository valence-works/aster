# Feature Specification: Portable Operator Expansion

**Feature Branch**: `009-portable-operators`  
**Created**: 2026-05-18  
**Status**: Draft  
**Input**: User description: "Add portable query operators NotEquals, In, StartsWith, and facet value Exists with provider capability declarations, validation, in-memory execution, SQLite JSON translation where straightforward, typed helper support, docs, and conformance tests."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Express Common Portable Predicates (Priority: P1)

As an SDK user, I want to express inequality, membership, prefix matching, and facet presence with the portable query AST so I can avoid provider-specific workarounds for common query shapes.

**Why this priority**: The current operator set covers equality, containment, and ranges but leaves frequent query shapes unmodeled.

**Independent Test**: Can be tested by constructing manual `ResourceQuery` instances with each new operator and asserting validation and execution results against supported providers.

**Acceptance Scenarios**:

1. **Given** resources with different metadata or facet values, **When** a caller uses `NotEquals`, **Then** matching excludes the specified value.
2. **Given** resources with values from a known set, **When** a caller uses `In`, **Then** matching includes any resource whose value is in the set.
3. **Given** string metadata or facet values, **When** a caller uses `StartsWith`, **Then** matching uses case-insensitive prefix semantics.
4. **Given** an aspect exists with or without a specific facet value, **When** a caller uses facet `Exists`, **Then** matching requires the named facet to be present.

---

### User Story 2 - Discover And Validate Operator Support (Priority: P1)

As a host or provider author, I want provider capabilities and validation to describe the new operators so unsupported shapes fail before execution when possible.

**Why this priority**: Existing query capability discovery is the contract between UI/API surfaces, validators, and providers.

**Independent Test**: Can be tested by inspecting capability declarations and validating supported and unsupported queries for built-in providers.

**Acceptance Scenarios**:

1. **Given** a built-in provider, **When** capabilities are inspected, **Then** supported new operators are listed for the appropriate filter categories.
2. **Given** a query using an unsupported operator for a filter category, **When** validation runs, **Then** it returns a structured unsupported comparison failure.
3. **Given** a provider declares support for a new operator, **When** conformance tests run, **Then** validation and execution agree.

---

### User Story 3 - Author New Operators From Typed Helpers (Priority: P2)

As an SDK user, I want typed facet helpers for the new operators so typed query authoring remains consistent with manual AST construction.

**Why this priority**: Slice 008 introduced typed query authoring ergonomics; the new operators should not force callers back to string-heavy manual construction.

**Independent Test**: Can be tested by generating each new facet operator from typed helpers and asserting the produced AST equals the manual shape.

**Acceptance Scenarios**:

1. **Given** a typed facet selection, **When** a caller creates `NotEqualTo`, `In`, `StartsWith`, or `Exists`, **Then** the resulting AST uses the resolved aspect key and facet identifier.
2. **Given** a typed helper-generated query, **When** it is validated and executed against a supporting provider, **Then** behavior matches the equivalent manual query.

### Edge Cases

- `In` MUST reject null, non-enumerable, string-as-enumerable, and empty candidate sets.
- `StartsWith` MUST remain string-oriented and fail validation or execution where a provider does not support prefix matching for that field category.
- `Exists` MUST distinguish a present facet from a missing facet, even when the containing aspect exists.
- New operators MUST preserve existing case-insensitive text semantics.
- Existing manual query construction MUST remain supported.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add operators directly to the existing AST, validators, and providers; do not introduce a planner or query framework.
- **Explicitness**: Provider capabilities MUST list supported operators by filter category.
- **Dependencies**: No new dependencies.
- **Operational Impact**: No storage, deployment, migration, or runtime configuration changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The portable comparison operator model MUST include `NotEquals`, `In`, `StartsWith`, and `Exists`.
- **FR-002**: In-memory query execution MUST support the new operators for appropriate metadata and facet filters.
- **FR-003**: SQLite JSON query execution MUST support the new operators where they can be translated explicitly over existing metadata and JSON facet values.
- **FR-004**: Provider capability declarations MUST list the new supported operators for each filter category.
- **FR-005**: Query validation MUST reject unsupported operators and invalid `In` candidate sets with structured failures.
- **FR-006**: Typed facet helpers SHOULD create manual-equivalent AST values for the new facet operators.
- **FR-007**: Provider conformance tests MUST cover supported and unsupported behavior for the new operators.
- **FR-008**: The feature MUST NOT introduce public `IQueryable<Resource>`, provider-specific SQL APIs, runtime scanning, automatic discovery, or a query planner.

### Key Entities

- **Comparison operator**: A portable operator applied by metadata and facet filters.
- **Membership value set**: The candidate values supplied to an `In` filter.
- **Facet existence predicate**: A facet value filter that matches when a named facet is present.
- **Provider capability declaration**: The machine-readable provider support contract used by validation and conformance tests.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Manual queries using all new supported operators validate and execute against at least one built-in provider.
- **SC-002**: SQLite JSON and in-memory capability tests assert the new operator support.
- **SC-003**: Typed helper tests cover all new facet operator helpers.
- **SC-004**: Provider conformance tests include the new supported operator cases.
- **SC-005**: Full solution tests and build pass.

## Assumptions

- `Exists` is modeled as a facet comparison operator on `FacetValueFilter`; the existing `Value` field is ignored for this operator.
- `In` values are supplied as non-string enumerable candidate sets.
- Prefix matching is case-insensitive, matching existing text comparison behavior.
