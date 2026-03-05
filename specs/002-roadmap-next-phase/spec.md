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

### Session 2026-03-05

- Q: What should happen when `ActivateAsync` is called without a mode for a channel that has no stored record yet? → A: Require explicit `ChannelMode` on first activation; return a typed validation error if omitted.
- Q: What should the system do when a retrieved resource's `DefinitionVersion` is not in the runtime definition store? → A: Return the resource as-is; `DefinitionVersion` is advisory only — definition resolution is the caller's concern.
- Q: What level of observability should the provider emit? → A: Structured logging via `ILogger` only: key lifecycle events, concurrency conflicts, and slow queries above a threshold.
- Q: How should provider schema versioning be scoped? → A: Phase 2 ships a single schema version; in-place upgrade is explicitly out of scope; breaking schema changes require a fresh database.
- Q: Should a dedicated success criterion cover channel mode durability after restart? → A: Yes — add SC-005.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Durable Resource Lifecycle (Priority: P1)

As a product team, we need resources and their versions to remain available after restart so that draft, active, and historical content is reliable in day-to-day operations.

**Why this priority**: Without durable storage, all other roadmap value is temporary and operationally unsafe.

**Independent Test**: Can be fully tested by creating resources, restarting the host, and verifying all versions and activation states are still present and retrievable.

**Acceptance Scenarios**:

1. **Given** existing resources with multiple versions, **When** the host restarts, **Then** all resources and versions remain available with unchanged identity and version history.
2. **Given** one version marked active in a channel with a stored `ChannelMode`, **When** the host restarts, **Then** the same version remains active in that channel and the stored `ChannelMode` is unchanged.

---

### User Story 2 - Persistent Querying for Operational Use (Priority: P2)

As an operator, I need to search persisted resources by core metadata and simple aspect values so I can find relevant resources quickly in non-demo workloads.

**Why this priority**: Persistence without practical filtering does not support real operational workflows.

**Independent Test**: Can be fully tested by loading mixed resource data and verifying query filters, sorting, and paging return expected results from stored data.

**Acceptance Scenarios**:

1. **Given** resources with varied metadata and aspect values, **When** a filter is applied, **Then** only matching resources are returned.
2. **Given** more results than one page, **When** paging and sorting are requested, **Then** the returned order and page boundaries are stable and correct.


### Edge Cases

- Concurrent save/activate requests on the same resource MUST return an optimistic concurrency conflict for one operation and preserve an unbroken append-only history.
- Queries that sort on a field missing in some records MUST still return all matched records, with missing sort values ordered last.
- Calling `ActivateAsync` on a channel with no existing activation record and no `ChannelMode` supplied MUST return a typed validation error (not default silently to any mode).
- How does the system behave when a requested resource version references a definition version that is no longer available in the runtime cache but still exists in storage? `DefinitionVersion` is advisory; the resource is returned as-is and definition resolution is the caller's concern.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST persist resource definitions (including embedded aspect and facet definitions), resources, and activation state so they survive process restarts.
- **FR-002**: The system MUST preserve append-only version history for each resource and prevent in-place mutation of historical versions.
- **FR-003**: Users MUST be able to retrieve latest, specific historical, and channel-active versions from persisted data.
- **FR-004**: The system MUST support persisted filtering by resource metadata and simple aspect values using equals, contains, and range semantics.
- **FR-005**: The system MUST support deterministic paging and sorting for persisted query results.
- **FR-006**: The system MUST enforce optimistic concurrency for conflicting updates, return a typed conflict outcome, and require caller-initiated retry without automatic overwrite.
- **FR-007**: The system MUST expose clear failure reasons when a query cannot be executed as requested.
- **FR-008**: The system MUST maintain behavioral parity with Phase 1 core lifecycle semantics for create, draft save, activate, deactivate, and retrieval.
- **FR-009**: Phase 2 MUST ship one production-grade reference provider based on SQLite with JSON document storage semantics.
- **FR-010**: Activation channel policy (`SingleActive` | `MultiActive`) MUST be stored durably per `(ResourceId, Channel)` pair, survive restarts, and be enforced on every subsequent activation within that channel. An explicit `ChannelMode` MUST be supplied on the first activation of a channel; omitting it MUST return a typed validation error.
- **FR-011**: Query sorting MUST include records with missing sort fields and order those missing values after all records with present sort values.
- **FR-012**: The provider MUST emit structured log entries via `ILogger` for key lifecycle events (definition register, resource create/update, activation change), concurrency conflicts, and queries that exceed a configurable slow-query threshold.

### Key Entities *(include if feature involves data)*

- **Resource Definition Record**: Persists an immutable version of a `ResourceDefinition`, including all embedded `AspectDefinition` and `FacetDefinition` snapshots. Identified by (`DefinitionId`, `Version`).
- **Resource Record**: Persists an immutable version snapshot of a `Resource` instance. Identified by (`ResourceId`, `Version`). Status (draft vs active) is derived from activation state, not stored.
- **Activation Record**: Persists the mutable channel activation state for a resource, tracking which version ordinals are active per channel and the durable `ChannelMode` policy (`SingleActive` | `MultiActive`). Maps to `ActivationState`.

## Non-Functional Requirements

### Observability
- The provider uses `ILogger<T>` (injected via DI) exclusively; no external telemetry dependency is introduced.
- Log levels: `Information` for normal lifecycle events; `Warning` for concurrency conflicts and slow queries; `Error` for unhandled failures.
- Slow-query threshold is configurable via provider options (default: 500 ms).
- No metrics or distributed tracing are in scope for Phase 2.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of resources and versions created during validation remain available and unchanged after restart and recovery checks.
- **SC-002**: At least 95% of standard persisted queries complete in under 2 seconds when validated against a dataset of 100k resource versions.
- **SC-003**: Query correctness validation shows at least 99% match between expected and actual results across metadata and aspect-value filters.
- **SC-004**: Concurrent update tests produce zero corrupted histories and return conflict outcomes for all intentionally conflicting operations.
- **SC-005**: After a host restart, every channel's stored `ChannelMode` is unchanged and continues to be enforced correctly on subsequent activations.

## Assumptions

- The next roadmap phase requested by the user maps to Phase 2: Persistence & Querying Essentials.
- The Phase 2 reference provider is SQLite + JSON, with extension points retained for additional providers later.
- The provider ships a single fixed schema version; in-place schema upgrades and multi-version migration are explicitly out of scope for Phase 2. A breaking schema change requires a fresh database.
- Query support in this phase focuses on baseline operational filters and does not include advanced capability negotiation.
- Operational performance validation uses a fixed dataset of 100k resource versions.
