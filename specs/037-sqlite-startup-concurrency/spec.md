# Feature Specification: SQLite Startup Concurrency Hardening

**Feature Branch**: `037-sqlite-startup-concurrency`  
**Created**: 2026-06-01  
**Status**: Draft  
**Input**: User description: "Continue remaining planned work with bounded operational hardening. Add SQLite JSON concurrent schema/provider initialization coverage without new APIs, schema changes, providers, public SQL surface, query planner behavior, schedulers, benchmarks, or dependencies unless a real concurrency defect is exposed."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Concurrent Fresh Startup Is Safe (Priority: P1)

As a host operator starting multiple application instances against the same new SQLite JSON database, I want concurrent provider initialization to complete predictably so startup does not create a broken or partial schema.

**Why this priority**: Fresh database startup is the highest-risk concurrent initialization path because all required tables may be created at the same time.

**Independent Test**: Run several concurrent SQLite JSON provider constructions against the same new database path, then verify all startup attempts complete and the schema can persist and read definitions/resources afterward.

**Acceptance Scenarios**:

1. **Given** an empty SQLite database path, **When** several SQLite JSON providers initialize concurrently, **Then** every initialization completes without corrupting the database.
2. **Given** concurrent initialization has completed for a fresh database, **When** a definition and resource are persisted and read back, **Then** persisted data remains usable through the existing provider APIs.

---

### User Story 2 - Concurrent Existing Startup Preserves Data (Priority: P2)

As a host operator restarting several application instances against an existing SQLite JSON database, I want concurrent provider initialization to preserve existing data and tenant-aware table shape.

**Why this priority**: Existing databases carry production state; concurrent restart must not rewrite, duplicate, or erase persisted data.

**Independent Test**: Seed a SQLite JSON database, run several concurrent provider constructions against the same path, then verify definitions, resources, activation state, lifecycle markers, and tenant-aware table shape remain intact.

**Acceptance Scenarios**:

1. **Given** an existing tenant-aware SQLite JSON database with persisted state, **When** several providers initialize concurrently, **Then** persisted state is unchanged after initialization.
2. **Given** concurrent initialization has completed for an existing database, **When** table metadata is inspected through test-only verification, **Then** tenant-aware key columns remain present and no bootstrap-only leftovers are introduced.

---

### User Story 3 - No-Schema Mode Remains Passive (Priority: P3)

As a host that disables automatic SQLite schema initialization, I want concurrent construction of identity/capability services to remain passive so operational policy is respected.

**Why this priority**: This protects an existing explicit operational contract while adding concurrency coverage elsewhere.

**Independent Test**: Construct SQLite JSON services concurrently with schema initialization disabled and verify no database file is created.

**Acceptance Scenarios**:

1. **Given** schema initialization is disabled, **When** several SQLite JSON services are constructed concurrently, **Then** no SQLite database file is created by passive service construction.

### Edge Cases

- Concurrent startup against a fresh database must not fail because another initializer created a table first.
- Concurrent startup against an existing tenant-aware database must not duplicate rows or remove tenant metadata.
- No-schema mode must remain passive even when multiple service providers are built at once.
- Tests must avoid broad timing-sensitive assertions; completion and final state are the observable contract.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Prefer focused regression tests. Production changes are allowed only for an exposed concurrency defect.
- **Explicitness**: The slice uses explicit SQLite JSON service registration and existing options; no scanning, discovery, or implicit provider registry is introduced.
- **Dependencies**: None.
- **Operational Impact**: Local validation gains concurrent startup coverage. Deployment, debugging, storage format, public APIs, query behavior, scheduling, and observability remain unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST include coverage proving concurrent SQLite JSON initialization against a fresh database completes without schema corruption.
- **FR-002**: The system MUST verify a database initialized concurrently can persist and read resource definitions and resources through existing APIs.
- **FR-003**: The system MUST include coverage proving concurrent SQLite JSON initialization against an existing tenant-aware database preserves persisted definitions, resources, activation state, lifecycle markers, and tenant-aware table shape.
- **FR-004**: The system MUST verify concurrent initialization does not introduce duplicate rows or bootstrap-only table leftovers in the covered existing-database scenario.
- **FR-005**: The system MUST preserve `InitializeSchema=false` passive behavior under concurrent service construction.
- **FR-006**: The system MUST NOT introduce new public APIs, provider registries, runtime scanning, automatic discovery, query planner behavior, public SQL, public `IQueryable<Resource>`, schedulers, benchmark infrastructure, storage schema changes, or third-party dependencies.
- **FR-007**: If tests expose a real SQLite startup concurrency defect, the fix MUST be the smallest explicit change that preserves existing provider behavior and storage shape.

### Key Entities

- **Concurrent Startup Attempt**: One provider/service construction path participating in the same startup window for a shared SQLite database path.
- **Fresh Startup Database**: A database path with no existing SQLite file before concurrent initialization begins.
- **Existing Startup Database**: A tenant-aware SQLite JSON database containing persisted definitions, resource versions, activation state, and lifecycle marker rows before concurrent initialization begins.
- **Passive No-Schema Construction**: Service construction with automatic schema initialization disabled, expected to avoid creating or mutating the database file.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Concurrent fresh-database startup is covered by an automated test that verifies successful initialization and subsequent read/write behavior.
- **SC-002**: Concurrent existing-database startup is covered by an automated test that verifies persisted state and tenant-aware table shape after initialization.
- **SC-003**: Concurrent no-schema construction is covered by an automated test that verifies no database file is created.
- **SC-004**: The full solution build and test suite pass without introducing new dependencies or public surface area.
