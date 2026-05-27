# Feature Specification: Policy Application Orchestration

**Feature Branch**: `017-policy-application-orchestration`  
**Created**: 2026-05-25  
**Status**: Draft  
**Input**: User description: "Add host-controlled policy application orchestration for applying previewed archive and soft-delete outcomes. Hosts should be able to submit policy preview candidate outcomes for explicit application, receive structured per-candidate results and diagnostics, reuse lifecycle marker idempotency, and avoid background schedulers, hidden retention jobs, destructive pruning writes, authorization policy engines, provider registries, public SQL, or IQueryable<Resource>."

## Clarifications

### Session 2026-05-27

- Q: How should application handle a preview candidate whose resource version is no longer the latest version? → A: Fail stale candidates when the candidate resource version is no longer the latest version.
- Q: How should application handle conflicting lifecycle outcomes for the same resource in one request? → A: Reject all same-resource candidates that request conflicting lifecycle outcomes in the same request.
- Q: Should application require the referenced policy declaration to still exist and match the candidate outcome? → A: Fail candidates when the referenced policy declaration is missing or no longer matches the requested outcome.
- Q: Should policy application add lifecycle hook coverage in this slice? → A: Policy application must not add new lifecycle hook behavior in this slice.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Apply Previewed Lifecycle Outcomes (Priority: P1)

As a host author, I need to explicitly apply selected archive and soft-delete candidates from a policy preview, so policy outcomes can move from report to action without hidden background execution.

**Why this priority**: Policy foundations already identify candidate outcomes. The next useful slice is the smallest host-controlled path that applies non-destructive lifecycle marker outcomes.

**Independent Test**: Create preview candidates for archive and soft-delete, submit selected candidates for application, and verify matching resources receive explicit lifecycle markers while resource versions and activation state remain unchanged.

**Acceptance Scenarios**:

1. **Given** a policy preview contains archive candidates, **When** the host submits those candidates for application, **Then** the matching resources receive archive lifecycle markers and the response identifies each applied candidate.
2. **Given** a policy preview contains soft-delete candidates, **When** the host submits those candidates for application, **Then** the matching resources receive soft-delete lifecycle markers and the response identifies each applied candidate.
3. **Given** a host applies only a subset of preview candidates, **When** application completes, **Then** only the selected resources are marked and unselected candidates remain unchanged.
4. **Given** a host applies lifecycle outcomes, **When** resource history and activation state are inspected, **Then** stored resource versions and activation channels remain intact.

---

### User Story 2 - Report Per-Candidate Application Results (Priority: P2)

As a host author, I need per-candidate application results and stable diagnostics, so I can show operators exactly which outcomes applied, were already satisfied, failed, or were skipped.

**Why this priority**: Applying policy outcomes usually happens in administrative or compliance workflows. Hosts need precise reporting rather than a single all-or-nothing status.

**Independent Test**: Submit a mixed application request containing valid candidates, already-marked resources, missing resources, stale version candidates, mismatched candidates, and unsupported pruning candidates; verify the response reports a stable result for every input item.

**Acceptance Scenarios**:

1. **Given** an input candidate already has the requested lifecycle marker, **When** the host applies it again, **Then** the response reports an idempotent success and does not create duplicate marker state.
2. **Given** an input candidate references a missing resource, **When** the host applies it, **Then** the response reports a stable target-not-found diagnostic for that candidate.
3. **Given** an input candidate references an unsupported or non-lifecycle outcome, **When** the host applies it, **Then** the response rejects that candidate with a stable preview-only or unsupported-outcome diagnostic.
4. **Given** some candidates fail and others are valid, **When** the host submits them together, **Then** valid candidates may still apply and every candidate receives its own result.

---

### User Story 3 - Bound Application To Explicit Host Intent (Priority: P3)

As a host author, I need policy application to remain bounded by explicit request data, tenant scope, and selected outcomes, so the SDK does not turn policy metadata into an implicit scheduler or authorization system.

**Why this priority**: Policy application must preserve the explicitness established by policy foundations. The SDK should provide a controlled application primitive, not an automatic policy runner.

**Independent Test**: Submit application requests with tenant scope, policy identifiers, candidate resource identifiers, and unsupported policy kinds; verify application does not cross tenant boundaries, does not re-evaluate hidden policy scopes, and does not execute pruning writes.

**Acceptance Scenarios**:

1. **Given** matching resource identifiers exist in multiple tenants, **When** a tenant-scoped host applies policy candidates for one tenant, **Then** only resources inside that tenant boundary are considered.
2. **Given** policy declarations exist but no host application request is submitted, **When** normal resource workflows run, **Then** no lifecycle markers are applied automatically.
3. **Given** a candidate represents version pruning, **When** a host submits it for application, **Then** the SDK reports that pruning remains preview-only and performs no destructive write.
4. **Given** a host submits a candidate that does not match the requested policy or outcome shape, **When** application runs, **Then** the candidate is diagnosed and no unrelated resource is marked.

### Edge Cases

- The same candidate appears more than once in one request.
- The same resource is submitted with both archive and soft-delete outcomes.
- A resource is already archived when a soft-delete candidate is submitted, or already soft-deleted when an archive candidate is submitted.
- A candidate references a resource version that is no longer the latest version.
- A candidate references a policy identifier that no longer exists on the current definition.
- A candidate references a policy declaration whose current outcome no longer matches the submitted candidate outcome.
- A candidate is missing policy, resource, or outcome identity.
- A tenant-scoped request references a resource outside the tenant boundary.
- A request contains zero candidates.
- A request contains both supported lifecycle candidates and unsupported pruning candidates.
- Application is retried after a partial failure.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is a host-invoked application operation over explicit candidate inputs that delegates marker writes to existing lifecycle marker behavior. Schedulers, policy engines, pruning writes, restore flows, provider registries, and automatic re-evaluation are out of scope.
- **Explicitness**: Hosts choose when to apply outcomes and which candidates to submit. The system MUST NOT discover, schedule, or execute policies through hidden conventions, ambient state, or background processing.
- **Dependencies**: None.
- **Operational Impact**: Existing local development, deployment, and debugging remain unchanged. No worker process, timer, external service, migration framework, or new provider infrastructure is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a host-controlled way to apply selected policy preview candidate outcomes.
- **FR-002**: Application MUST support archive and soft-delete lifecycle outcomes from preview candidates.
- **FR-003**: Application MUST NOT support destructive version pruning writes in this slice.
- **FR-004**: Application MUST reject pruning candidates with a stable preview-only diagnostic and perform no destructive write.
- **FR-005**: Application MUST treat policy declarations and preview candidates as inputs to explicit host action, not as automatic execution triggers.
- **FR-006**: Application requests MUST be bounded by one effective tenant scope.
- **FR-007**: Omitted tenant scope MUST continue to mean the documented default single-tenant scope.
- **FR-008**: Application MUST NOT apply markers outside the effective tenant boundary.
- **FR-009**: Each input candidate MUST produce a per-candidate result.
- **FR-010**: Per-candidate results MUST distinguish applied, already satisfied, skipped, and failed outcomes.
- **FR-011**: Per-candidate diagnostics MUST be stable enough for hosts and tests to distinguish missing target, unsupported outcome, preview-only pruning, conflicting lifecycle state, invalid candidate shape, and tenant-scoped target-not-found behavior.
- **FR-012**: Applying a lifecycle outcome MUST use the same effective lifecycle marker semantics as direct marker writes: same-marker writes are idempotent and conflicting archive/soft-delete states are rejected.
- **FR-013**: Applying lifecycle outcomes MUST NOT rewrite resource versions, deactivate active versions, delete resources, or mutate policy declarations.
- **FR-014**: Application MUST allow hosts to submit a subset of preview candidates rather than requiring an entire preview result to be applied.
- **FR-015**: Duplicate candidates in one request MUST NOT create duplicate marker state and MUST produce deterministic per-candidate results.
- **FR-016**: Candidates with missing resource identity, missing policy identity, invalid outcome, or unsupported target shape MUST be rejected with diagnostics and without marker writes.
- **FR-017**: Candidates that include a resource version MUST fail as stale when that version is no longer the latest version for the resource in the effective tenant scope.
- **FR-018**: Candidates for the same resource that request conflicting lifecycle outcomes in one request MUST all be rejected for that resource before either conflicting outcome is applied.
- **FR-019**: Candidates MUST fail when the referenced policy declaration is missing from the current resource definition or no longer matches the requested lifecycle outcome.
- **FR-020**: Application MUST NOT introduce background schedulers, hidden retention jobs, authorization or permission policy engines, cross-tenant application, runtime scanning, provider registries, public SQL, public `IQueryable<Resource>`, destructive pruning writes, or restore workflows.
- **FR-021**: Policy application MUST NOT add new lifecycle hook behavior in this slice.
- **FR-022**: Existing direct lifecycle marker writes, policy previews, resource writes, activation behavior, queries, portability, and lifecycle hooks MUST remain deterministic when policy application is introduced.
- **FR-023**: Documentation MUST explain host-controlled application, per-candidate results, idempotency, tenant boundary behavior, stale candidate behavior, policy declaration mismatch behavior, conflicting same-resource outcome behavior, lifecycle hook non-goals, pruning preview-only behavior, and out-of-scope automatic execution.

### Key Entities *(include if feature involves data)*

- **Policy Application Request**: Host-provided request containing tenant scope and selected preview candidates to apply.
- **Policy Application Candidate**: Host-selected item derived from a preview candidate, identifying policy, outcome, resource, and optional resource version context.
- **Policy Application Result**: Overall response containing per-candidate outcomes, counts, and diagnostics.
- **Candidate Application Result**: Per-candidate status describing whether the candidate was applied, already satisfied, skipped, or failed.
- **Application Diagnostic**: Stable diagnostic describing invalid input, unsupported outcome, preview-only pruning, missing target, conflict, or tenant-scoped target-not-found behavior.
- **Lifecycle Marker Outcome**: Archive or soft-delete marker state that can be explicitly applied to a resource.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can apply selected archive and soft-delete preview candidates and observe lifecycle markers on the selected resources only.
- **SC-002**: Every submitted candidate receives exactly one per-candidate result with a stable status and diagnostics when applicable.
- **SC-003**: Reapplying an already satisfied lifecycle candidate produces an idempotent success without duplicate marker state.
- **SC-004**: Submitting pruning candidates produces preview-only diagnostics and no resource version deletion.
- **SC-005**: Tenant-scoped application returns zero applied markers outside the effective tenant boundary.
- **SC-006**: A stale version candidate is rejected with a stable diagnostic and does not apply a lifecycle marker.
- **SC-007**: Conflicting archive and soft-delete candidates for the same resource in one request are rejected without applying either conflicting marker.
- **SC-008**: A candidate whose policy declaration is missing or outcome-mismatched is rejected with a stable diagnostic and does not apply a lifecycle marker.
- **SC-009**: Existing lifecycle hook behavior remains unchanged by policy application.
- **SC-010**: Existing policy preview, direct lifecycle marker, query, portability, and resource version tests continue to pass without requiring application orchestration.

## Assumptions

- Hosts remain responsible for deciding when to apply preview outcomes.
- Preview candidate shape from policy foundations is the source of truth for selectable candidate identity.
- Archive and soft-delete are the only write-side outcomes in this slice.
- Restore workflows, marker transitions, destructive pruning writes, schedulers, compliance interpretation, and authorization remain future or host responsibilities.
