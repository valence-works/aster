# Feature Specification: SQLite Schema Idempotency Hardening

**Feature Branch**: `036-sqlite-schema-idempotency`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Continue remaining planned work with bounded operational hardening. Add SQLite JSON schema initialization idempotency coverage without new APIs, schema changes, providers, public SQL surface, query planner behavior, schedulers, benchmarks, or dependencies."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reopen Initialized SQLite Stores (Priority: P1)

As a host developer, I want repeated SQLite provider initialization against the same database to preserve data and table shape so process restarts do not alter persisted state.

**Why this priority**: SQLite schema initialization runs from store/query provider construction. Operational restarts must be idempotent.

**Independent Test**: Create a SQLite provider, save a definition/resource/activation/marker, dispose it, reopen the provider more than once, and verify persisted content remains readable and schema columns remain tenant-aware.

**Acceptance Scenarios**:

1. **Given** a database initialized by `AddAsterSqliteJson`, **When** providers are built repeatedly for the same path, **Then** existing definitions, resource versions, activations, and lifecycle markers remain readable.
2. **Given** a repeatedly initialized database, **When** table metadata is inspected, **Then** tenant-aware primary keys and expected columns remain unchanged.

---

### User Story 2 - Rerun Legacy Tenant Upgrade Safely (Priority: P2)

As a maintainer, I want legacy pre-tenant SQLite tables to upgrade once and tolerate later initialization runs so startup does not re-copy data or leave bootstrap tables.

**Why this priority**: The provider supports legacy table upgrade. Idempotency should be pinned down after the first upgrade.

**Independent Test**: Create pre-tenant legacy tables with resource data, initialize SQLite provider, dispose it, initialize again, then verify default-tenant data is preserved and no legacy bootstrap tables remain.

**Acceptance Scenarios**:

1. **Given** legacy tables without tenant columns, **When** schema initialization runs, **Then** rows are migrated into default-tenant rows.
2. **Given** the migrated database, **When** schema initialization runs again, **Then** rows are not duplicated and no `__legacy_tenant_bootstrap` tables remain.

---

### User Story 3 - Preserve Explicit No-Initialization Mode (Priority: P3)

As a host developer, I want `InitializeSchema = false` to remain explicit so resolving provider identity/capabilities does not create or mutate a database.

**Why this priority**: Operational simplicity includes predictable startup side effects. This existing behavior must remain protected while adding idempotency coverage.

**Independent Test**: Register SQLite JSON with `InitializeSchema = false`, resolve provider identity/capabilities, and verify the database file is not created.

**Acceptance Scenarios**:

1. **Given** SQLite registration with schema initialization disabled, **When** only identity/capabilities are resolved, **Then** no database file is created.
2. **Given** this hardening slice, **When** tests run, **Then** no service registration or provider behavior change is needed for no-initialization mode.

### Edge Cases

- Tests MUST use isolated temporary database files and clean up WAL/SHM companions.
- Tests MUST assert final persisted state, not only absence of exceptions.
- Legacy upgrade tests MUST assert no bootstrap tables remain after repeated initialization.
- If hardening exposes a defect, the fix MUST be narrowly scoped to existing SQLite schema initialization behavior.
- This slice MUST NOT introduce new public APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, schedulers, benchmark infrastructure, or dependencies.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Prefer focused regression tests; production changes are allowed only for an exposed idempotency defect.
- **Explicitness**: Tests use explicit database paths, provider options, tenants, and table metadata checks.
- **Dependencies**: None.
- **Operational Impact**: No runtime setup, deployment, storage format, or observability changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST include SQLite provider reopen coverage proving repeated initialization preserves persisted definitions, resource versions, activations, and lifecycle markers.
- **FR-002**: The system MUST include table metadata coverage proving initialized tables retain tenant-aware primary keys after repeated initialization.
- **FR-003**: The system MUST include legacy pre-tenant table upgrade idempotency coverage.
- **FR-004**: The system MUST verify repeated legacy-upgrade initialization does not duplicate rows.
- **FR-005**: The system MUST verify no legacy bootstrap tables remain after repeated initialization.
- **FR-006**: The system MUST preserve `InitializeSchema = false` no-database-creation behavior for identity/capability resolution.
- **FR-007**: The system MUST NOT introduce new product APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, schedulers, benchmark infrastructure, or dependencies.

### Key Entities

- **SQLite Initialization Scenario**: Repeated construction of SQLite-backed services against the same database.
- **Legacy Tenant Upgrade Scenario**: Existing pre-tenant tables upgraded to tenant-aware primary keys.
- **No-Initialization Scenario**: SQLite registration with schema initialization disabled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Focused hardening tests prove repeated SQLite initialization preserves persisted state and tenant-aware table shape.
- **SC-002**: Legacy upgrade tests prove reruns do not duplicate migrated rows or leave bootstrap tables.
- **SC-003**: No-initialization tests prove identity/capability resolution still avoids database creation.
- **SC-004**: Full test suite and build pass without new dependencies, product APIs, schema changes, or provider behavior expansion.
