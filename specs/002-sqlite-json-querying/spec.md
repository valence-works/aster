# Feature Specification: SQLite JSON Querying (Phase 2A)

**Feature Branch**: `002-sqlite-json-querying`  
**Created**: 2026-05-15  
**Status**: Draft  
**Input**: User description: "Create the Spec Kit feature spec for SQLite JSON query translation, provider capability/error behavior, and SQLite-backed query execution."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Query Persisted Resources By Metadata (Priority: P1)

Developers using the SQLite JSON provider can execute `ResourceQuery` against persisted resources without first loading every version into an in-memory LINQ evaluator.

**Why this priority**: This is the smallest useful provider-backed query slice. It proves the SQLite provider can execute the portable query model directly while keeping the behavior simple and observable.

**Independent Test**: Persist several resource versions through `IResourceManager`, recreate the service provider against the same SQLite database, query through `IResourceQueryService`, and verify metadata filters, version scopes, paging, and metadata sorting are executed correctly.

**Acceptance Scenarios**:

1. **Given** persisted `Product` and `Order` resources, **When** a query specifies `DefinitionId = "Product"`, **Then** only persisted Product resources are returned.
2. **Given** a resource with versions 1 and 2, **When** a query uses `ResourceVersionScope.Latest`, **Then** only version 2 is returned.
3. **Given** a resource with versions 1 and 2, **When** a query uses `ResourceVersionScope.AllVersions`, **Then** both versions are returned in deterministic order when a sort is supplied.
4. **Given** active and draft resources, **When** a query uses `ResourceVersionScope.Active` with `ActivationChannel = "Published"`, **Then** only versions active in that channel are returned.
5. **Given** a query with `Skip`, `Take`, and metadata sort expressions, **When** it runs against SQLite, **Then** paging is applied after filtering and sorting.

---

### User Story 2 - Filter Persisted Facet Values In SQLite JSON (Priority: P2)

Developers can query persisted aspect/facet values stored inside SQLite JSON payloads using the existing portable `FacetValueFilter` model.

**Why this priority**: Facet querying is the core reason for the query AST. It should build on the simpler metadata/scope implementation after the provider-backed query path is proven.

**Independent Test**: Persist resources with typed aspect payloads, recreate the service provider, execute facet-value queries through `IResourceQueryService`, and verify `Equals`, `Contains`, and `Range` behavior for simple scalar facets.

**Acceptance Scenarios**:

1. **Given** persisted Product resources with `TitleAspect.Title`, **When** a `Contains` query searches for `"Gadget"`, **Then** only matching resources are returned.
2. **Given** persisted Product resources with `PriceAspect.Amount`, **When** a `RangeValue` query specifies `Min = 10` and `Max = 100`, **Then** only resources within the inclusive range are returned.
3. **Given** persisted resources with missing aspect keys or missing facets, **When** a facet filter targets the missing value, **Then** those resources do not match.
4. **Given** a named aspect key such as `"PriceAspect:Sale"`, **When** a facet filter targets that key, **Then** SQLite JSON lookup uses the exact aspect key and facet path.

---

### User Story 3 - Reject Unsupported Query Shapes Explicitly (Priority: P3)

Developers receive a typed, actionable error when the SQLite provider cannot execute a query shape with portable semantics.

**Why this priority**: Provider-backed querying must be honest. Failing explicitly is preferable to silent client-side fallback or provider-specific behavior that looks portable but is not.

**Independent Test**: Execute intentionally unsupported query shapes against the SQLite query service and verify `UnsupportedQueryFeatureException` identifies the unsupported predicate, sort, or scope.

**Acceptance Scenarios**:

1. **Given** a query with an unsupported metadata field, **When** it runs against SQLite, **Then** the provider throws `UnsupportedQueryFeatureException`.
2. **Given** a query with a facet sort the provider does not yet support, **When** it runs against SQLite, **Then** the provider throws `UnsupportedQueryFeatureException` instead of sorting in memory.
3. **Given** a query that attempts facet sorting, **When** facet sorting has not been implemented for SQLite, **Then** the provider rejects the query with a typed error.

---

### Edge Cases

- Active scope without `ActivationChannel` MUST fail before executing SQL.
- `RangeValue` with both `Min` and `Max` as `null` SHOULD be treated as unsupported because it does not constrain results.
- `Skip` and `Take` values below zero MUST be rejected.
- `Take = 0` MUST return an empty result set without error.
- Missing JSON aspect keys or facet properties MUST evaluate as non-matches, not errors.
- SQLite `NULL`, missing JSON properties, and empty strings MUST NOT be treated as equal unless explicitly supported by a future query operator.
- Facet paths MUST be built safely and MUST NOT allow SQL injection through aspect keys or facet identifiers.
- Query result ordering MUST be deterministic when sorts are supplied.
- Provider behavior MUST be the same after service/provider restart against the same SQLite database.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Implement direct SQLite query translation for the currently supported `ResourceQuery` model. Do not introduce a generic query planner, expression-tree compiler, or cross-provider capability framework in this slice.
- **Explicitness**: SQLite-backed query execution is enabled through explicit DI registration. Unsupported query shapes fail with `UnsupportedQueryFeatureException`; no hidden fallback to in-memory evaluation.
- **Dependencies**: No new runtime dependency is expected beyond the existing `Microsoft.Data.Sqlite` package already used by `Aster.Persistence.SqliteJson`.
- **Operational Impact**: Queries run against the existing SQLite database file. Local development remains `dotnet test`; no external database server, background service, or migration runner is required for this slice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The SQLite JSON provider MUST expose an `IResourceQueryService` implementation that executes supported `ResourceQuery` instances against SQLite.
- **FR-002**: The SQLite query service MUST support `ResourceQuery.DefinitionId` filtering.
- **FR-003**: The SQLite query service MUST support `ResourceVersionScope.Latest`, `AllVersions`, `Active`, and `Draft`.
- **FR-004**: `ResourceVersionScope.Active` MUST require `ActivationChannel`.
- **FR-005**: The SQLite query service MUST support `MetadataFilter` for `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, and `Created`.
- **FR-006**: The SQLite query service MUST support logical `And`, `Or`, and `Not` over supported child predicates.
- **FR-007**: The SQLite query service MUST support metadata sorting for supported metadata fields.
- **FR-008**: The SQLite query service MUST apply `Skip` and `Take` after filtering and sorting.
- **FR-009**: The SQLite query service MUST support `AspectPresenceFilter` against persisted JSON payloads.
- **FR-010**: The SQLite query service MUST support `FacetValueFilter` with `Equals` for simple scalar JSON facet values.
- **FR-011**: The SQLite query service SHOULD support `FacetValueFilter` with `Contains` for simple string JSON facet values.
- **FR-012**: The SQLite query service SHOULD support `FacetValueFilter` with `Range` for numeric simple scalar JSON facet values.
- **FR-013**: Unsupported query shapes MUST throw `UnsupportedQueryFeatureException`.
- **FR-014**: The provider MUST NOT silently fall back to in-memory filtering, sorting, or paging for unsupported predicates.
- **FR-015**: SQL generation MUST use parameters for user-supplied values and MUST safely encode JSON paths for aspect/facet lookup.
- **FR-016**: The SQLite provider registration SHOULD replace the default `IResourceQueryService` when `AddAsterSqliteJson(...)` is called after `AddAsterCore()`.
- **FR-017**: Existing in-memory query behavior MUST remain unchanged.
- **FR-018**: Tests MUST cover persisted data across service-provider/store recreation.
- **FR-019**: This feature MUST NOT expose `IQueryable<Resource>` or implement a LINQ provider.
- **FR-020**: Typed LINQ-like query helpers MAY be introduced by a later feature, but they MUST compile into `ResourceQuery` rather than bypassing the AST.

### Key Entities *(include if feature involves data)*

- **ResourceQuery**: Portable query request containing version scope, optional definition shortcut, filter tree, sort expressions, skip, and take.
- **FilterExpression**: Query predicate tree containing metadata, aspect presence, facet value, and logical expressions.
- **SortExpression**: Ordering instruction over metadata fields for this slice; facet sorting is explicitly unsupported unless implemented and tested.
- **RangeValue**: Inclusive/exclusive lower and upper bounds for range comparisons.
- **SQLite Resource Payload**: JSON serialized `Resource` snapshot persisted in `resource_versions.payload`.
- **SQLite Activation Payload**: JSON serialized `ActivationState` persisted in `activation_states.payload`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Metadata/scope/paging/sorting queries over persisted SQLite resources pass integration tests after recreating the service provider.
- **SC-002**: Facet `Equals`, string `Contains`, and numeric `Range` behavior is covered by integration tests for simple scalar JSON values.
- **SC-003**: Unsupported query shapes consistently throw `UnsupportedQueryFeatureException` with no silent in-memory fallback.
- **SC-004**: The full solution test suite passes with no new warnings.
- **SC-005**: SQLite provider setup remains a single explicit `AddAsterSqliteJson(...)` registration and requires no external service.

## Assumptions

- The existing SQLite provider schema remains table-plus-JSON-payload based for this slice.
- JSON facet lookup targets simple aspect objects stored under `Resource.Aspects[aspectKey]`.
- Full provider capability negotiation (`IQueryCapabilities`) is out of scope for this spec; typed unsupported errors are sufficient.
- Typed LINQ-like AST authoring helpers are out of scope for this spec. The intended future direction is a small expression-subset compiler that produces `ResourceQuery`, not an `IQueryable` provider.
- Date-like facet ranges, full-text search, culture-aware collation, diacritic normalization, array operators, and facet sorting are out of scope unless explicitly added by a later spec.
- The current in-memory query service remains the reference behavior for unsupported SQLite work during development, but production execution MUST NOT silently fall back.
