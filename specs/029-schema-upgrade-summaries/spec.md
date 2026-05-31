# Feature Specification: Schema Upgrade Summaries

**Feature Branch**: `029-schema-upgrade-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Add pure host-facing summaries for resource schema status and schema upgrade results so hosts can display deterministic upgrade readiness and application outcomes without re-walking result objects. Keep the slice bounded: no storage changes, no provider changes, no service registration, no scheduler, no audit persistence, no public SQL, no public IQueryable<Resource>, and no mutation behavior beyond existing schema upgrade operations."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Schema Status Results (Priority: P1)

As a host author, I need a deterministic summary over inspected resource schema statuses, so that upgrade readiness screens can show counts for current, outdated, missing, and unknown-lineage resources without duplicating counting logic.

**Why this priority**: Status inspection is the read-only entry point for schema upgrade decisions; hosts need stable aggregate information before deciding whether to apply upgrades.

**Independent Test**: Can be tested by creating a collection of schema status results with mixed statuses and verifying a summary exposes total counts, upgrade-needed counts, blocking counts, and deterministic status counts.

**Acceptance Scenarios**:

1. **Given** schema status results containing current and older-than-latest resources, **When** the host creates a summary, **Then** the summary reports total inspected resources and deterministic counts by schema status.
2. **Given** schema status results containing missing definitions, missing definition versions, or unknown resource lineage, **When** the host creates a summary, **Then** the summary reports blocking and unknown-lineage counts separately from upgrade-needed counts.
3. **Given** an empty schema status result collection, **When** the host creates a summary, **Then** all counts are zero and status count collections are empty.

---

### User Story 2 - Summarize Schema Upgrade Results (Priority: P2)

As a host author, I need a deterministic summary over schema upgrade outcomes, so that upgrade execution screens can show how many resources were upgraded, no-oped, and carried forward undeclared aspect data.

**Why this priority**: Upgrade application already returns result objects; hosts need a simple, repeatable aggregate view for reporting successful and no-op outcomes.

**Independent Test**: Can be tested by creating upgraded and no-op schema upgrade results and verifying the summary reports outcome counts, created-version counts, carried-forward aspect counts, and source/target version counts.

**Acceptance Scenarios**:

1. **Given** schema upgrade results containing upgraded and no-op outcomes, **When** the host creates a summary, **Then** the summary reports deterministic counts by upgrade status.
2. **Given** upgraded results with carried-forward aspect keys, **When** the host creates a summary, **Then** the summary reports total carried-forward aspect key count and deterministic counts by aspect key.
3. **Given** upgrade results with source and target definition versions, **When** the host creates a summary, **Then** the summary reports deterministic source and target definition version counts.

---

### User Story 3 - Preserve Pure, Bounded Behavior (Priority: P3)

As a maintainer, I need schema summary helpers to remain pure transformations over existing result objects, so that the feature does not change schema upgrade execution, storage, provider behavior, or operational complexity.

**Why this priority**: This slice is a reporting affordance; widening it into execution orchestration or storage would violate the current bounded roadmap.

**Independent Test**: Can be tested by verifying summaries can be created from in-memory result objects without service registration or provider access and that existing schema upgrade tests continue to pass.

**Acceptance Scenarios**:

1. **Given** existing schema status and upgrade result objects, **When** summaries are created, **Then** no store, provider, scheduler, service registration, or mutation behavior is required.
2. **Given** existing schema upgrade operations, **When** the summary helpers are introduced, **Then** existing upgrade behavior, exceptions, lifecycle hooks, and SQLite persistence behavior remain unchanged.

### Edge Cases

- Empty result collections produce zero counts and empty count lists.
- Null result collection inputs are treated as empty only for collection-level helpers; calling an extension on a null result object fails with normal argument validation.
- Null nested collections on result objects are treated as empty where result object contracts allow them.
- Unknown source definition versions are counted separately from concrete version numbers.
- Blank carried-forward aspect keys are ignored in key-specific counts but still do not cause summary creation to fail.
- Status and version count lists are ordered deterministically so UI tests and host output remain stable.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The acceptable behavior is a small set of immutable summary records and pure extension helpers. Batch execution, orchestration, auditing, persistence, and provider integration are intentionally out of scope.
- **Explicitness**: Hosts explicitly call `ToSummary()` on existing result objects or collections. There is no runtime scanning, hidden registration, or implicit background work.
- **Dependencies**: None.
- **Operational Impact**: No deployment, migration, local development, provider setup, or observability changes. Debugging remains ordinary object inspection and unit tests.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a host-facing summary for a collection of `ResourceSchemaStatusResult` objects.
- **FR-002**: The schema status summary MUST expose the total inspected resource-version count.
- **FR-003**: The schema status summary MUST expose deterministic counts by `ResourceSchemaStatus`.
- **FR-004**: The schema status summary MUST expose an upgrade-needed count for resources whose status indicates they are older than the latest definition version.
- **FR-005**: The schema status summary MUST expose a blocking count for resources whose status indicates a missing definition or missing definition version.
- **FR-006**: The schema status summary MUST expose an unknown-lineage count for resources whose status indicates missing resource lineage.
- **FR-007**: The system MUST provide a host-facing summary for a collection of `ResourceSchemaUpgradeResult` objects.
- **FR-008**: The schema upgrade summary MUST expose the total processed result count.
- **FR-009**: The schema upgrade summary MUST expose deterministic counts by `ResourceSchemaUpgradeStatus`.
- **FR-010**: The schema upgrade summary MUST expose how many results produced an upgraded resource version.
- **FR-011**: The schema upgrade summary MUST expose total carried-forward aspect key count and deterministic counts by aspect key, ignoring blank keys.
- **FR-012**: The schema upgrade summary MUST expose deterministic source definition version counts and target definition version counts, including a stable bucket for unknown source versions.
- **FR-013**: Summary helpers MUST be pure transformations over supplied result objects and MUST NOT read or write stores, execute upgrades, register services, call providers, schedule work, persist audit records, expose raw SQL, expose public `IQueryable<Resource>`, or mutate result objects.
- **FR-014**: Summary helpers MUST preserve existing schema status and upgrade behavior, including exceptions, lifecycle hooks, tenant behavior, and provider persistence semantics.

### Key Entities *(include if feature involves data)*

- **Resource Schema Status Summary**: Aggregate view over one or more schema status inspection results, including total inspected count, status counts, upgrade-needed count, blocking count, and unknown-lineage count.
- **Resource Schema Upgrade Summary**: Aggregate view over one or more schema upgrade results, including processed count, upgrade status counts, upgraded resource count, carried-forward aspect counts, and definition version counts.
- **Schema Status Count**: Deterministic count for one `ResourceSchemaStatus` value.
- **Schema Upgrade Status Count**: Deterministic count for one `ResourceSchemaUpgradeStatus` value.
- **Schema Definition Version Count**: Deterministic count for one source or target definition version bucket.
- **Carried-Forward Aspect Key Count**: Deterministic count for one nonblank aspect key carried forward during upgrade.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can create a schema status summary from mixed status results and obtain correct total, upgrade-needed, blocking, unknown-lineage, and per-status counts without reimplementing aggregation.
- **SC-002**: Hosts can create a schema upgrade summary from upgraded and no-op results and obtain correct processed, upgraded-resource, per-status, carried-forward aspect, and version counts without reimplementing aggregation.
- **SC-003**: Empty result collections produce zero-valued summaries with empty count collections.
- **SC-004**: Existing schema status, schema upgrade, lifecycle hook, tenant, and SQLite behavior tests continue to pass unchanged except for focused tests added for summaries.
- **SC-005**: The feature introduces no new dependencies, storage schema changes, provider changes, service registrations, schedulers, audit persistence, public SQL, public `IQueryable<Resource>`, or mutation behavior.
