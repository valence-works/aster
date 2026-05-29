# Feature Specification: Lifecycle Restore Workflows

**Feature Branch**: `018-lifecycle-restore-workflows`  
**Created**: 2026-05-27  
**Status**: Draft  
**Input**: User description: "Add host-controlled lifecycle restore workflows for archive and soft-delete markers. Hosts should be able to preview and explicitly restore archived or soft-deleted resources by clearing lifecycle marker state without rewriting resource versions, changing activation state, adding schedulers, authorization engines, provider registries, public SQL, IQueryable<Resource>, or destructive pruning writes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Restore Marked Resources Explicitly (Priority: P1)

As a host author, I need to explicitly restore archived or soft-deleted resources, so operators can reverse a lifecycle marker without mutating resource history or activation state.

**Why this priority**: Policy foundations and application orchestration can mark resources as archived or soft-deleted. The smallest useful next step is the matching host-controlled way to clear those reversible marker states.

**Independent Test**: Mark resources as archived and soft-deleted, submit selected restore candidates, and verify the matching lifecycle markers are cleared while resource versions and activation state remain unchanged.

**Acceptance Scenarios**:

1. **Given** a resource has an archive marker, **When** the host submits an archive restore candidate, **Then** the marker is cleared and the resource is no longer considered archived.
2. **Given** a resource has a soft-delete marker, **When** the host submits a soft-delete restore candidate, **Then** the marker is cleared and the resource is no longer considered soft-deleted.
3. **Given** a host restores only a subset of marked resources, **When** restoration completes, **Then** only the selected resources are restored and unselected markers remain unchanged.
4. **Given** a host restores lifecycle markers, **When** resource history and activation state are inspected, **Then** stored resource versions and activation channels remain intact.

---

### User Story 2 - Preview Restore Outcomes Before Writing (Priority: P2)

As a host author, I need to preview restore candidates before applying them, so an operator can see which resources can be restored, are already unmarked, are missing, or have mismatched marker state.

**Why this priority**: Restore is operator-facing and should mirror the explicit policy preview/application model. A dry-run preview prevents accidental marker removal and gives hosts stable UI/reporting data.

**Independent Test**: Submit a mixed restore preview request containing restorable resources, already-restored resources, missing resources, and marker-state mismatches; verify each input receives a stable preview outcome without changing marker state.

**Acceptance Scenarios**:

1. **Given** a resource has the expected lifecycle marker, **When** restore preview runs, **Then** the response reports it as restorable and no marker is cleared.
2. **Given** a resource has no lifecycle marker, **When** restore preview runs, **Then** the response reports it as already restored.
3. **Given** a resource has a different lifecycle marker than the requested restore state, **When** restore preview runs, **Then** the response reports a stable marker-mismatch diagnostic.
4. **Given** a host previews restore candidates, **When** normal resource workflows run afterward, **Then** no lifecycle state changes have occurred from preview alone.

---

### User Story 3 - Report Deterministic Restore Results (Priority: P3)

As a host author, I need per-candidate restore results and stable diagnostics, so restore workflows can show exactly what changed, what was already satisfied, and what failed.

**Why this priority**: Restore requests can be batched across many resources. Hosts need deterministic partial-success reporting and retries without hidden behavior.

**Independent Test**: Submit a mixed restore application request with valid candidates, duplicates, missing targets, already-restored resources, tenant misses, and mismatched marker state; verify each input receives exactly one stable result.

**Acceptance Scenarios**:

1. **Given** duplicate restore candidates for the same resource and marker state, **When** application runs, **Then** marker state is cleared at most once and duplicate results are deterministic.
2. **Given** some restore candidates fail and others are valid, **When** the host submits them together, **Then** valid candidates may still restore and every candidate receives its own result.
3. **Given** matching resource identifiers exist in multiple tenants, **When** a tenant-scoped restore request runs, **Then** only the effective tenant is considered.
4. **Given** no restore request is submitted, **When** policy declarations, previews, or normal lifecycle operations are used, **Then** no lifecycle marker is cleared automatically.

### Edge Cases

- A restore request contains zero candidates.
- A candidate is missing resource identity or expected lifecycle marker state.
- A candidate requests restore for an unsupported state such as active, retained, pruning, or an unknown outcome.
- A resource has no lifecycle marker when restore is requested.
- A resource has archive state when soft-delete restore is requested, or soft-delete state when archive restore is requested.
- A resource no longer exists in the effective tenant, but a stale restore candidate is submitted.
- A tenant-scoped request references a resource outside the tenant boundary.
- The same candidate appears more than once in one request.
- Preview is run and then marker state changes before application.
- Restore is retried after a partial failure.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is a host-invoked preview and application operation over explicit restore candidates. Schedulers, policy engines, automatic restore, destructive pruning writes, provider registries, and broad state-transition frameworks are out of scope.
- **Explicitness**: Hosts choose when to preview restore candidates, when to apply them, which resources to restore, and which marker state is expected. The system MUST NOT discover, schedule, authorize, or execute restore behavior through hidden conventions or background processing.
- **Dependencies**: None.
- **Operational Impact**: Existing local development, deployment, and debugging remain unchanged. No worker process, timer, external service, migration framework, provider infrastructure, or new storage engine is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a host-controlled way to preview lifecycle restore candidates before applying writes.
- **FR-002**: The system MUST provide a host-controlled way to apply selected lifecycle restore candidates.
- **FR-003**: Restore MUST support clearing archive and soft-delete lifecycle marker states.
- **FR-004**: Restore MUST NOT support destructive version pruning, resource deletion, resource-version rewrites, activation changes, or policy declaration mutation.
- **FR-005**: Restore requests MUST be bounded by one effective tenant scope.
- **FR-006**: Omitted tenant scope MUST continue to mean the documented default single-tenant scope.
- **FR-007**: Restore MUST NOT clear markers outside the effective tenant boundary.
- **FR-008**: Each restore preview input MUST produce a per-candidate preview outcome.
- **FR-009**: Each restore application input MUST produce a per-candidate application result.
- **FR-010**: Restore preview outcomes MUST distinguish restorable, already restored, skipped, and failed candidates.
- **FR-011**: Restore application results MUST distinguish restored, already restored, skipped, and failed candidates.
- **FR-012**: Restore diagnostics MUST be stable enough for hosts and tests to distinguish invalid candidate shape, unsupported restore state, missing target, tenant-scoped target-not-found behavior, and marker-state mismatch.
- **FR-013**: A restore candidate MUST identify the target resource and the expected lifecycle marker state to clear.
- **FR-014**: A candidate with missing resource identity, missing expected state, unsupported state, or unknown state MUST fail with diagnostics and without clearing any marker.
- **FR-015**: A candidate for a resource without a lifecycle marker MUST report already restored and MUST NOT fail solely because the marker is absent.
- **FR-016**: A candidate whose current marker state differs from the expected state MUST fail with a stable marker-mismatch diagnostic and MUST NOT clear the current marker.
- **FR-017**: Duplicate restore candidates in one request MUST NOT create nondeterministic results and MUST clear marker state at most once.
- **FR-018**: Restore preview MUST be non-mutating.
- **FR-019**: Restore application MUST allow partial success while still returning exactly one result for every candidate.
- **FR-020**: Restore MUST integrate with existing lifecycle-state query behavior so restored resources no longer match archived or soft-deleted lifecycle filters after marker removal.
- **FR-021**: Restore MUST preserve existing direct lifecycle marker writes, policy previews, policy application, resource writes, activation behavior, queries, portability, and lifecycle hooks unless this feature explicitly defines a restore behavior.
- **FR-022**: Restore MUST NOT introduce background schedulers, hidden retention jobs, authorization or permission policy engines, cross-tenant restore, runtime scanning, provider registries, public SQL, public `IQueryable<Resource>`, destructive pruning writes, or a general state-transition framework.
- **FR-023**: Documentation MUST explain host-controlled restore preview, restore application, idempotency, tenant boundary behavior, marker mismatch behavior, non-mutating preview, lifecycle hook non-goals, and out-of-scope automatic execution.

### Key Entities *(include if feature involves data)*

- **Lifecycle Restore Request**: Host-provided request containing tenant scope and selected restore candidates.
- **Lifecycle Restore Candidate**: Host-selected item identifying a resource and the expected archive or soft-delete marker state to clear.
- **Lifecycle Restore Preview Result**: Non-mutating response containing one preview outcome per candidate.
- **Lifecycle Restore Application Result**: Write response containing one application outcome per candidate.
- **Restore Candidate Result**: Per-candidate status describing whether the candidate is restorable/restored, already restored, skipped, or failed.
- **Restore Diagnostic**: Stable diagnostic describing invalid input, unsupported state, missing target, marker mismatch, or tenant-scoped target-not-found behavior.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can preview restore candidates and receive exactly one non-mutating outcome for every submitted candidate.
- **SC-002**: A host can restore selected archived and soft-deleted resources and observe that only selected lifecycle markers are cleared.
- **SC-003**: Restoring lifecycle markers does not change resource version history or activation state.
- **SC-004**: Restored resources no longer match archived or soft-deleted lifecycle-state query criteria.
- **SC-005**: Already-restored resources produce idempotent already-restored outcomes without error or marker writes.
- **SC-006**: Marker-state mismatch candidates fail with stable diagnostics and do not clear the current marker.
- **SC-007**: Tenant-scoped restore returns zero marker changes outside the effective tenant boundary.
- **SC-008**: Restore preview performs no writes.
- **SC-009**: Existing policy application, direct lifecycle marker, query, portability, and resource version tests continue to pass.

## Assumptions

- Restore means clearing an archive or soft-delete lifecycle marker, not creating a new resource version or changing activation channels.
- Hosts remain responsible for deciding whether an operator is allowed to restore a resource.
- Restore candidates require an expected marker state so stale or mismatched UI selections fail closed.
- Archive and soft-delete are the only reversible lifecycle marker states in this slice.
- Resource deletion, destructive pruning, scheduler behavior, automatic policy execution, authorization, and broad workflow/state-machine behavior remain future or host responsibilities.
