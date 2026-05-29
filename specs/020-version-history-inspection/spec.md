# Feature Specification: Resource Version History Inspection

**Feature Branch**: `020-version-history-inspection`
**Created**: 2026-05-29
**Status**: Implemented
**Input**: User description: "Add a read-only host-facing resource version history inspection workflow. Hosts can request a tenant-scoped history for one resource and receive ordered version summaries that identify latest, draft, active channels, lifecycle marker state, definition version, timestamps, and safe maintenance hints. The feature must reuse existing stores and services, introduce no storage schema changes, no query planner, no public SQL, no IQueryable<Resource>, no provider registry, no background jobs, and no mutation behavior."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect One Resource History (Priority: P1)

As a host author, I need to request the version history for one resource in one tenant, so that management screens and maintenance workflows can show the current version timeline without assembling state manually.

**Why this priority**: This is the minimum useful behavior and supports existing policy application, restore, and pruning workflows.

**Independent Test**: Can be fully tested by creating several resource versions, activating selected versions, requesting history, and verifying ordered summaries mark latest, draft, and active states correctly.

**Acceptance Scenarios**:

1. **Given** a resource has multiple versions in the default tenant, **When** the host requests its history, **Then** every version is returned in deterministic version order with latest and draft state indicated.
2. **Given** selected versions are active in one or more channels, **When** the host requests history, **Then** each summary identifies the active channels for that version.
3. **Given** the resource does not exist in the effective tenant, **When** the host requests history, **Then** the result is empty and no exception or mutation occurs.

---

### User Story 2 - Include Lifecycle and Maintenance Signals (Priority: P2)

As a host author, I need history summaries to include lifecycle marker state and safe maintenance hints, so that users can understand whether versions are visible, restorable, or protected before applying policy actions.

**Why this priority**: Lifecycle and policy workflows landed before this slice; history inspection should make those states visible without creating another write workflow.

**Independent Test**: Can be fully tested by marking a resource archived or soft-deleted, requesting history, and verifying summaries report the lifecycle state and conservative maintenance hints.

**Acceptance Scenarios**:

1. **Given** a resource has an archive or soft-delete marker, **When** the host requests history, **Then** each returned version summary includes the current lifecycle marker state for the resource.
2. **Given** a version is latest or active, **When** the host requests history, **Then** that version is identified as protected from destructive pruning.
3. **Given** a version is historical, inactive, and not latest, **When** the host requests history, **Then** that version is identified as a possible maintenance candidate without claiming that policy pruning will definitely apply.

---

### User Story 3 - Preserve Tenant Boundaries and Provider Compatibility (Priority: P3)

As a tenant-aware host author, I need version history inspection to resolve exactly one tenant and work with existing in-memory and SQLite-backed providers, so that history displays cannot leak cross-tenant state.

**Why this priority**: Tenant isolation is a core Phase 5 invariant and SQLite compatibility keeps the feature useful outside tests.

**Independent Test**: Can be fully tested by creating matching resource identifiers in two tenants, requesting history for one tenant, and verifying only that tenant's versions, activation channels, and lifecycle marker appear.

**Acceptance Scenarios**:

1. **Given** matching resource identifiers exist in two tenants, **When** history is requested for tenant A, **Then** no version, activation, or lifecycle state from tenant B appears.
2. **Given** omitted tenant scope is used, **When** history is requested, **Then** the documented default single-tenant scope is used.
3. **Given** SQLite JSON persistence is active, **When** history is requested, **Then** the same observable summaries are returned as the in-memory provider for equivalent state.

### Edge Cases

- Resource identifier is null, empty, or whitespace.
- Resource has versions but no active versions in any channel.
- Resource is active in multiple channels or multiple active versions exist in one channel.
- Resource has no lifecycle marker.
- Resource exists in another tenant but not the effective tenant.
- Concurrent writes occur while history is being assembled; the result must remain read-only and must not infer stronger consistency than the underlying store provides.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature SHOULD add one small read-only service over existing resource, activation, and lifecycle state. It MUST NOT introduce a query planner, registry, scheduler, or workflow engine.
- **Explicitness**: Hosts MUST request history for an explicit resource identifier and optional tenant scope. No runtime scanning, ambient tenant discovery, or automatic maintenance behavior is introduced.
- **Dependencies**: None.
- **Operational Impact**: The feature SHOULD require no schema migration, deployment change, background process, new provider package, or local development setup change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a host-facing read-only workflow for inspecting one resource's version history.
- **FR-002**: History requests MUST resolve exactly one effective tenant.
- **FR-003**: Omitted tenant scope MUST continue to mean the documented default single-tenant scope.
- **FR-004**: History inspection MUST NOT create, update, activate, deactivate, restore, prune, mark, or otherwise mutate resource state.
- **FR-005**: History results MUST include the effective tenant and the requested resource identifier.
- **FR-006**: History results MUST include one ordered summary for each resource version found in the effective tenant.
- **FR-007**: Version summaries MUST identify resource version number, resource definition identifier, resource definition version, creation timestamp, latest status, draft status, active channel names, lifecycle marker state, and destructive-pruning protection status.
- **FR-008**: Version summaries MUST treat latest versions and versions active in any channel as protected from destructive pruning.
- **FR-009**: Version summaries MAY identify historical inactive non-latest versions as possible maintenance candidates, but MUST NOT guarantee policy eligibility without policy evaluation.
- **FR-010**: Missing resources in the effective tenant MUST return an empty history result rather than leaking whether the same resource exists in another tenant.
- **FR-011**: Invalid request shape, including missing or blank resource identity, MUST fail with argument validation consistent with existing core SDK patterns.
- **FR-012**: History inspection MUST preserve provider-agnostic behavior for the core SDK and MUST work with existing in-memory and SQLite JSON registrations.
- **FR-013**: History inspection MUST NOT introduce storage schema changes, public raw SQL, public `IQueryable<Resource>`, provider registries, runtime scanning, background jobs, automatic policy execution, or broad workflow/state-machine infrastructure.
- **FR-014**: Documentation MUST explain read-only behavior, tenant boundaries, lifecycle fields, active-channel interpretation, maintenance hints, and non-goals.

### Key Entities *(include if feature involves data)*

- **Version History Request**: Host-provided request containing a resource identifier and optional tenant scope.
- **Version History Result**: Ordered read-only response containing the effective tenant, requested resource identifier, and version summaries.
- **Version Summary**: Read-only description of one resource version, including definition version, timestamps, latest/draft/active/lifecycle state, and conservative maintenance hints.
- **Maintenance Hint**: Non-authoritative signal describing whether a version is protected from destructive pruning or only a possible candidate for later policy evaluation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can inspect a resource with at least five versions and correctly identify latest, draft, active, and historical inactive versions from one call.
- **SC-002**: Tenant isolation tests demonstrate that matching resource identifiers in two tenants return only the requested tenant's history.
- **SC-003**: Equivalent in-memory and SQLite JSON test arrangements produce equivalent version summary semantics.
- **SC-004**: Existing policy, lifecycle restore, pruning, portability, and query tests continue to pass unchanged.
