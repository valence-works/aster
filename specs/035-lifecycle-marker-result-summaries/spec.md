# Feature Specification: Lifecycle Marker Result Summaries

**Feature Branch**: `035-lifecycle-marker-result-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Next slice after portable validation summaries. Continue the bounded host-reporting workstream by adding pure summaries for lifecycle marker write results without changing marker service behavior, storage, providers, public SQL, or public IQueryable<Resource>."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Marker Write Results (Priority: P1)

As a host developer, I want to summarize a lifecycle marker write result so I can report whether the write succeeded, what marker state is effective, and which resource was affected without manually inspecting the result object.

**Why this priority**: `ResourceLifecycleMarkerResult` is a host-facing write result used by policy and lifecycle workflows, but hosts currently need custom code to summarize success and marker state.

**Independent Test**: Construct successful and failed marker results, call the summary helper, and verify success, marker presence, marker state counts, affected resource counts, total diagnostics, and diagnostic code counts.

**Acceptance Scenarios**:

1. **Given** a successful marker result with an archived marker, **When** a host creates a summary, **Then** the summary reports success, marker present, archived state count, affected resource count, and zero diagnostics.
2. **Given** a failed marker result with diagnostics and no marker, **When** a host creates a summary, **Then** the summary reports failure, no marker, diagnostic counts, and no affected marker resource.

---

### User Story 2 - Aggregate Multiple Marker Results (Priority: P2)

As a host developer, I want to summarize a batch of manually collected lifecycle marker results so I can produce deterministic reporting for multiple marker writes.

**Why this priority**: Hosts often apply marker writes over selected resources. A small enumerable helper keeps reporting consistent without adding a batch service or workflow layer.

**Independent Test**: Construct a mixed set of marker results and verify deterministic success/failure counts, marker state counts, affected resource counts, diagnostic path counts, and diagnostic code counts.

**Acceptance Scenarios**:

1. **Given** multiple marker results with archived and soft-deleted markers, **When** a host creates a summary, **Then** marker state counts are deterministic by state.
2. **Given** repeated marker resources and repeated diagnostic resources, **When** a host creates a summary, **Then** distinct marker resource count and diagnostic resource counts are reported deterministically.

---

### User Story 3 - Preserve Marker Service Behavior (Priority: P3)

As a library maintainer, I want marker result summaries to remain pure projections so marker writes, stores, policies, providers, and existing service registrations remain unchanged.

**Why this priority**: This is a reporting affordance only. It must not become a new marker workflow, registry, storage concern, or mutation path.

**Independent Test**: Run focused summary tests and existing lifecycle marker service tests, then run full solution validation.

**Acceptance Scenarios**:

1. **Given** existing lifecycle marker service behavior, **When** the summary helper is added, **Then** marker apply behavior and diagnostics remain unchanged.
2. **Given** existing in-memory and SQLite marker behavior, **When** the summary helper is added, **Then** no provider or storage files need to change.

### Edge Cases

- Null single `ResourceLifecycleMarkerResult` input MUST throw `ArgumentNullException`.
- Null enumerable input MUST be treated as empty.
- Null result entries inside an enumerable MUST be ignored.
- Null `Diagnostics` collections MUST be treated as empty.
- Blank diagnostic codes, paths, and resource identifiers MUST NOT appear in keyed counts.
- Marker state counts MUST be ordered by enum value.
- String-key counts MUST be ordered using ordinal string ordering.
- Totals MUST include results and diagnostics even when keyed fields are blank.

### Constitution Alignment *(mandatory)*

- **Simplicity**: This feature SHOULD add summary records and extension methods over existing result objects. It MUST NOT add services, registries, providers, schedulers, stores, or workflow infrastructure.
- **Explicitness**: Hosts explicitly call `ToSummary()` on marker results. There is no automatic discovery, runtime scanning, implicit reporting pipeline, or hidden side effect.
- **Dependencies**: None. The implementation MUST use existing C#/.NET and project test dependencies only.
- **Operational Impact**: No deployment, storage, provider, configuration, or observability changes. Local validation remains standard `dotnet test` and `dotnet build`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a public summary type for one or more `ResourceLifecycleMarkerResult` objects.
- **FR-002**: The summary MUST expose total result count, success count, failure count, marker-present count, missing-marker count, and total diagnostic count.
- **FR-003**: The summary MUST expose whether all supplied results succeeded and whether any supplied result has diagnostics.
- **FR-004**: The summary MUST expose deterministic counts by `ResourceLifecycleMarkerState` for results that include markers.
- **FR-005**: The summary MUST expose deterministic distinct marker resource counts for nonblank marker resource identifiers.
- **FR-006**: The summary MUST expose deterministic diagnostic code counts using existing policy diagnostic code count shape where practical.
- **FR-007**: The summary MUST expose deterministic diagnostic path and resource identifier counts for nonblank policy diagnostic fields.
- **FR-008**: The single-result helper MUST throw `ArgumentNullException` for null root input.
- **FR-009**: The enumerable helper MUST treat null result collections as empty and ignore null entries.
- **FR-010**: The helper MUST treat null nested diagnostics as empty.
- **FR-011**: The implementation MUST preserve existing lifecycle marker service, marker store, policy, provider, storage, and registration behavior.
- **FR-012**: The implementation MUST NOT introduce storage changes, provider-specific behavior, service registration, a reporting framework, public raw SQL, public `IQueryable<Resource>`, query planner behavior, or mutation behavior.

### Key Entities *(include if feature involves data)*

- **Lifecycle Marker Result Summary**: Host-facing aggregate view over one or more marker write results; includes success/failure totals, marker state counts, marker resource counts, and diagnostic counts.
- **Lifecycle Marker State Count**: Deterministic count of result markers by `ResourceLifecycleMarkerState`.
- **Lifecycle Marker Resource Count**: Deterministic count of nonblank marker resource identifiers.
- **Policy Diagnostic Counts**: Deterministic counts over marker result diagnostics by code, path, and resource identifier.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can summarize one marker result or an enumerable of marker results through public `ToSummary()` calls.
- **SC-002**: Mixed marker results produce deterministic success, failure, marker state, marker resource, and diagnostic counts in tests regardless of input order.
- **SC-003**: Null collections, null entries, null diagnostics, and blank keyed fields are handled without host-side guard code.
- **SC-004**: Existing lifecycle marker service tests and full solution validation continue to pass without provider, storage, or service behavior changes.
