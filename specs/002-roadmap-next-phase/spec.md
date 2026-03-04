# Feature Specification: Persistence & Querying Essentials (Phase 2)

**Feature Branch**: `002-roadmap-next-phase`  
**Created**: 2026-03-04  
**Status**: Draft  
**Input**: User description: "Proceed with the next phase of the roadmap"

## Clarifications

### Session 2026-03-04

- Q: Which reference backend should Phase 2 implement first? → A: SQLite + JSON.
- Q: How should concurrent updates be handled? → A: Optimistic concurrency conflict; client retries intentionally.
- Q: What validation dataset size should performance targets use? → A: 100k resource versions.
- Q: What activation policy should channels use? → A: Configurable per channel (single-active or multi-active).
- Q: How should sorting handle missing field values? → A: Include all records; missing values always sort last.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Durable Resource Lifecycle (Priority: P1)

As a product team, we need resources and their versions to remain available after restart so that draft, active, and historical content is reliable in day-to-day operations.

**Why this priority**: Without durable storage, all other roadmap value is temporary and operationally unsafe.

**Independent Test**: Can be fully tested by creating resources, restarting the host, and verifying all versions and activation states are still present and retrievable.

**Acceptance Scenarios**:

1. **Given** existing resources with multiple versions, **When** the host restarts, **Then** all resources and versions remain available with unchanged identity and version history.
2. **Given** one version marked active in a channel, **When** the host restarts, **Then** the same version remains active in that channel.

---

### User Story 2 - Persistent Querying for Operational Use (Priority: P2)

As an operator, I need to search persisted resources by core metadata and simple aspect values so I can find relevant resources quickly in non-demo workloads.

**Why this priority**: Persistence without practical filtering does not support real operational workflows.

**Independent Test**: Can be fully tested by loading mixed resource data and verifying query filters, sorting, and paging return expected results from stored data.

**Acceptance Scenarios**:

1. **Given** resources with varied metadata and aspect values, **When** a filter is applied, **Then** only matching resources are returned.
2. **Given** more results than one page, **When** paging and sorting are requested, **Then** the returned order and page boundaries are stable and correct.

---

### User Story 3 - Deterministic Infrastructure Readiness (Priority: P3)

As a host administrator, I need infrastructure setup and upgrades to be deterministic so new environments can be initialized and existing ones upgraded with predictable outcomes.

**Why this priority**: Teams need repeatable provisioning and upgrade behavior to reduce deployment risk.

**Independent Test**: Can be fully tested by initializing an empty environment, applying infrastructure steps, and validating that subsequent runs are idempotent and leave the environment in the same expected state.

**Acceptance Scenarios**:

1. **Given** an empty environment, **When** infrastructure setup is executed, **Then** required storage structures are created and resource operations become available.
2. **Given** an environment already at target version, **When** setup is executed again, **Then** no destructive or duplicate changes occur.

### Edge Cases

- Concurrent save/activate requests on the same resource MUST return an optimistic concurrency conflict for one operation and preserve an unbroken append-only history.
- Queries that sort on a field missing in some records MUST still return all matched records, with missing sort values ordered last.
- What happens when infrastructure setup is interrupted mid-run and retried?
- How does the system behave when requested resource versions reference a definition version that is no longer available in the runtime cache but still exists in storage?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST persist resource definitions, resources, resource versions, and activation state so they survive process restarts.
- **FR-002**: The system MUST preserve append-only version history for each resource and prevent in-place mutation of historical versions.
- **FR-003**: Users MUST be able to retrieve latest, specific historical, and channel-active versions from persisted data.
- **FR-004**: The system MUST support persisted filtering by resource metadata and simple aspect values using equals, contains, and range semantics.
- **FR-005**: The system MUST support deterministic paging and sorting for persisted query results.
- **FR-006**: The system MUST enforce optimistic concurrency for conflicting updates, return a typed conflict outcome, and require caller-initiated retry without automatic overwrite.
- **FR-007**: The system MUST provide a provider-owned infrastructure setup process that can initialize an empty environment and safely re-run.
- **FR-008**: The system MUST allow hosts to choose automatic infrastructure setup at startup or explicit manual execution.
- **FR-009**: The system MUST expose clear failure reasons when a query cannot be executed as requested or when required infrastructure is unavailable.
- **FR-010**: The system MUST maintain behavioral parity with Phase 1 core lifecycle semantics for create, draft save, activate, deactivate, and retrieval.
- **FR-011**: Phase 2 MUST ship one production-grade reference provider based on SQLite with JSON document storage semantics.
- **FR-012**: Activation behavior MUST be configurable per channel to enforce either single-active or multi-active version policy.
- **FR-013**: Query sorting MUST include records with missing sort fields and order those missing values after all records with present sort values.

### Key Entities *(include if feature involves data)*

- **Definition Snapshot**: An immutable schema snapshot used to shape resource versions at creation time; includes identity and version ordinal.
- **Resource Version Record**: An immutable version entry tied to one resource identity; includes lifecycle state context and references to a definition snapshot.
- **Activation Record**: A channel-scoped marker indicating whether a given resource version is active, including activation metadata.
- **Query Request**: A structured request describing filters, operators, sorting, and paging expectations.
- **Infrastructure Step Record**: A tracked setup/upgrade step with unique identity and applied state used to ensure deterministic initialization and upgrades.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of resources and versions created during validation remain available and unchanged after restart and recovery checks.
- **SC-002**: At least 95% of standard persisted queries complete in under 2 seconds when validated against a dataset of 100k resource versions.
- **SC-003**: Query correctness validation shows at least 99% match between expected and actual results across metadata and aspect-value filters.
- **SC-004**: Concurrent update tests produce zero corrupted histories and return conflict outcomes for all intentionally conflicting operations.
- **SC-005**: Infrastructure setup from an empty environment succeeds in one run, and repeated runs complete without unintended state changes.

## Assumptions

- The next roadmap phase requested by the user maps to Phase 2: Persistence & Querying Essentials.
- The Phase 2 reference provider is SQLite + JSON, with extension points retained for additional providers later.
- Query support in this phase focuses on baseline operational filters and does not include advanced capability negotiation.
- Operational performance validation uses a fixed dataset of 100k resource versions.
