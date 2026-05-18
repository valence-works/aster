# Feature Specification: SQLite Date-Like Facet Ranges

**Feature Branch**: `010-sqlite-date-ranges`  
**Created**: 2026-05-18  
**Status**: Implemented
**Input**: User description: "Add SQLite JSON support for date-like facet range filters with an explicit accepted serialization contract, provider capability updates, validation alignment, SQL translation, conformance coverage, docs, and fail-closed diagnostics for unsupported or invalid date-like shapes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Query Date-Like Facet Ranges In SQLite (Priority: P1)

As an SDK user using the SQLite JSON provider, I want range filters over date-like facet values to work when values are stored in the documented portable shape, so time-based resources can be queried without provider-specific workarounds.

**Why this priority**: SQLite currently supports numeric facet ranges but intentionally rejects date-like facet ranges, leaving an important gap between in-memory and SQLite query behavior.

**Independent Test**: Can be tested by storing resources with date/time facet values, issuing a `FacetValueFilter` with `ComparisonOperator.Range`, and asserting SQLite returns only resources inside the requested interval.

**Acceptance Scenarios**:

1. **Given** resources with facet values serialized as accepted date/time strings, **When** a caller filters with a date-like `RangeValue`, **Then** SQLite returns resources whose facet values fall within the requested range.
2. **Given** inclusive and exclusive range bounds, **When** a caller executes the query, **Then** SQLite honors the bound inclusivity flags.
3. **Given** a one-sided date-like range, **When** a caller executes the query, **Then** SQLite treats the missing bound as unbounded.

---

### User Story 2 - Validate Date-Like Range Support Consistently (Priority: P1)

As a host or provider author, I want SQLite capability declarations and validation to describe date-like facet range support accurately, so callers can preflight queries before execution.

**Why this priority**: Capability declarations are the contract used by query UIs, validators, and conformance tests.

**Independent Test**: Can be tested by inspecting SQLite capabilities and validating supported date-like range queries.

**Acceptance Scenarios**:

1. **Given** SQLite JSON capabilities, **When** callers inspect facet range support, **Then** numeric and date-like shapes are declared.
2. **Given** a date-like range query with supported bound values, **When** validation runs, **Then** validation succeeds.
3. **Given** an unsupported range value shape, **When** validation runs, **Then** validation fails with a structured value-shape failure.

---

### User Story 3 - Fail Closed For Invalid Stored Date Values (Priority: P2)

As an SDK user, I want invalid or mixed stored facet values to be ignored or rejected predictably, so bad data does not produce misleading matches.

**Why this priority**: SQLite JSON documents may contain strings, numbers, nulls, or malformed values; date-like range behavior must remain explicit and debuggable.

**Independent Test**: Can be tested by storing valid and invalid facet values and asserting range filters only match valid accepted values.

**Acceptance Scenarios**:

1. **Given** a stored facet value that is not in the accepted date/time string shape, **When** a date-like range filter executes, **Then** that resource does not match.
2. **Given** a missing or null facet value, **When** a date-like range filter executes, **Then** that resource does not match.
3. **Given** invalid query bound values, **When** validation or execution runs, **Then** the query fails closed with a structured unsupported query failure.

### Edge Cases

- Date-like range bounds MUST accept `DateTime`, `DateTimeOffset`, and accepted date/time strings.
- Accepted stored date/time strings MUST be ISO-8601-style values that SQLite can compare lexicographically after normalization by the provider.
- Date-only values, local ambiguous formats, malformed strings, numbers, booleans, objects, arrays, missing facets, and null facet values MUST NOT match date-like range predicates.
- Numeric facet ranges MUST continue to behave unchanged.
- In-memory date-like range support MUST remain unchanged.
- The feature MUST NOT introduce a query planner, indexing model, migration, public SQL API, runtime scanning, or `IQueryable<Resource>`.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add direct SQLite translation for supported date-like range shapes; do not introduce indexing, planning, or schema migration.
- **Explicitness**: Document the accepted date/time storage contract and declare support through existing capabilities.
- **Dependencies**: None.
- **Operational Impact**: No storage migration, deployment change, or new runtime configuration.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: SQLite JSON capabilities MUST declare `QueryValueShape.DateTime` support for facet range filters.
- **FR-002**: SQLite JSON validation MUST accept facet range filters whose bounds are date-like values supported by the existing query value-shape model.
- **FR-003**: SQLite JSON execution MUST translate supported date-like facet range filters into parameterized SQL over existing JSON facet values.
- **FR-004**: SQLite JSON execution MUST continue to support numeric facet ranges exactly as before.
- **FR-005**: SQLite JSON execution MUST NOT match missing, null, malformed, non-string, or unsupported stored facet values for date-like range filters.
- **FR-006**: Date-like range translation MUST honor inclusive and exclusive bounds, and support min-only and max-only ranges.
- **FR-007**: Unsupported range value shapes MUST fail closed with structured `UnsupportedQueryFeatureException`/validation failures.
- **FR-008**: Provider conformance and SQLite query tests MUST cover date-like facet range validation and execution.
- **FR-009**: Documentation MUST explain the accepted stored date/time shape and current exclusions.

### Key Entities

- **Date-like facet range**: A `FacetValueFilter` using `ComparisonOperator.Range` and a `RangeValue` whose bounds are date-like.
- **Accepted stored date value**: A facet JSON scalar string that follows the documented date/time representation and can be compared consistently.
- **Range bound**: The minimum or maximum value in a range predicate, including inclusivity flags.
- **Provider capability declaration**: The SQLite provider support contract consumed by validation and conformance tests.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: SQLite JSON queries using date-like facet ranges validate and return correct results for inclusive, exclusive, and one-sided bounds.
- **SC-002**: SQLite JSON capability tests assert both numeric and date-like facet range support.
- **SC-003**: Provider conformance tests include a supported date-like range case for providers that declare date-like range support.
- **SC-004**: Invalid stored date-like facet values do not match date-like range filters.
- **SC-005**: `dotnet test Aster.sln`, `dotnet build Aster.sln`, and `git diff --check` pass.

## Assumptions

- Stored date/time facets are accepted when they are JSON string scalars in round-trip ISO-8601 format, such as values produced by `System.Text.Json` for `DateTime` or `DateTimeOffset`.
- Query bounds are normalized by SQLite translation before comparison.
- Date-only support remains out of scope until the query value-shape model explicitly includes it.
