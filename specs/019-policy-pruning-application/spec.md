# Feature Specification: Policy Pruning Application

**Feature Branch**: `019-policy-pruning-application`  
**Created**: 2026-05-29  
**Status**: Draft  
**Input**: User description: "Add host-controlled policy pruning application workflows. Hosts can apply selected version-pruning preview outcomes to permanently remove eligible non-latest, inactive resource versions within one tenant after explicit safety preflight. Preserve append-only behavior by default; require preview-derived candidates, stable per-candidate diagnostics, tenant scoping, idempotent already-pruned outcomes, and provider-backed atomicity where possible. Do not add schedulers, automatic retention jobs, authorization engines, provider registries, runtime scanning, public SQL, public IQueryable<Resource>, broad workflow/state-machine infrastructure, or schema migrations."

## Clarifications

### Session 2026-05-29

- Q: What preview basis is required for pruning candidates? → A: Candidates use existing preview fields: policy ID, policy kind, prune-preview outcome, resource ID, and resource version. No opaque preview token is introduced in this slice.
- Q: How is stale policy selection detected? → A: Application revalidates the current policy declaration by policy ID, kind, outcome, and current criteria against the candidate version before removal.
- Q: What retained-version safety floor applies? → A: Application must leave at least the policy's current maximum retained version count for the resource and must never prune latest or active versions.
- Q: Is pruning application all-or-nothing? → A: Cross-candidate all-or-nothing behavior is not required. Providers should remove each valid candidate conditionally and return per-candidate outcomes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preview-Selected Pruning Application (Priority: P1)

As a host author, I need to explicitly apply selected version-pruning preview outcomes, so that obsolete resource versions can be removed only after a host-controlled review and selection step.

**Why this priority**: Version pruning is destructive. The minimum useful workflow must prove that pruning is never automatic and only applies to host-selected candidates that came from a prior preview.

**Independent Test**: Can be fully tested by creating resources with multiple versions, generating pruning preview outcomes, selecting a subset, applying them, and verifying only the selected eligible versions are removed.

**Acceptance Scenarios**:

1. **Given** a pruning preview identifies eligible historical inactive versions, **When** the host submits selected candidates from that preview, **Then** the selected versions are removed and every input receives a stable result.
2. **Given** a preview includes several eligible versions for the same resource, **When** the host applies only some candidates, **Then** only the selected candidates are removed and unselected versions remain.
3. **Given** the host submits an empty application request, **When** pruning application runs, **Then** no versions are removed and the result is empty.

---

### User Story 2 - Fail-Closed Safety Preflight (Priority: P2)

As a host author, I need pruning application to re-check current version, activation, lifecycle, and policy state before removal, so that stale or unsafe candidates cannot remove the wrong data.

**Why this priority**: A preview can become stale before a host applies it. Safety preflight prevents destructive writes when the current state no longer matches the host-selected candidate.

**Independent Test**: Can be fully tested by previewing pruning candidates, changing resource state before application, and verifying stale or unsafe candidates fail with diagnostics and no removal.

**Acceptance Scenarios**:

1. **Given** a candidate version became latest after preview, **When** the host applies the old candidate, **Then** that candidate fails and the version remains.
2. **Given** a candidate version became active after preview, **When** the host applies the old candidate, **Then** that candidate fails and the version remains.
3. **Given** a pruning request would remove every retained version for a resource, **When** the host applies it, **Then** unsafe candidates fail with diagnostics and protected versions remain.

---

### User Story 3 - Tenant-Bounded Deterministic Results (Priority: P3)

As a tenant-aware host author, I need pruning application to operate inside exactly one effective tenant and report deterministic per-candidate outcomes, so that destructive maintenance is auditable and cannot cross tenant boundaries.

**Why this priority**: Tenant isolation and predictable result accounting are required before hosts can safely expose pruning controls in multi-tenant environments.

**Independent Test**: Can be fully tested by creating matching resource and version identifiers in two tenants, applying pruning in one tenant, and verifying the other tenant remains unchanged.

**Acceptance Scenarios**:

1. **Given** matching resources exist in two tenants, **When** pruning application runs for one tenant, **Then** only versions in that tenant are considered or removed.
2. **Given** duplicate pruning candidates are submitted, **When** application runs, **Then** the first candidate determines the outcome and later duplicates receive deterministic skipped or already-pruned outcomes.
3. **Given** a candidate was already pruned by an earlier request, **When** the host submits it again, **Then** the result reports an idempotent already-pruned outcome instead of failing unexpectedly.

### Edge Cases

- A null or malformed request is submitted.
- A candidate omits policy identity, policy kind, prune-preview outcome, resource identity, or resource version.
- A candidate references a resource or version that no longer exists in the effective tenant.
- A candidate references a version that exists outside the effective tenant.
- A candidate references a version that is latest, active, no longer matches current policy criteria, or is otherwise protected by current policy safety rules.
- A stale candidate no longer matches the policy declaration or preview basis that originally produced it.
- Multiple candidates target the same version in one request.
- Some candidates are valid while others are stale, invalid, missing, or unsafe.
- A provider can remove only part of a submitted batch.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is a host-invoked application operation over explicit pruning candidates with fail-closed safety checks. Schedulers, automatic retention jobs, authorization engines, provider registries, public query languages, and broad state-machine infrastructure are out of scope.
- **Explicitness**: Hosts explicitly submit the tenant scope, selected candidates, expected version identity, and preview basis. No runtime scanning, automatic discovery, hidden jobs, or ambient tenant context are introduced.
- **Dependencies**: None.
- **Operational Impact**: Local development, deployment, debugging, and observability remain straightforward. The feature adds no background worker, migration step, external service, or deployment-time maintenance process.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a host-controlled pruning application workflow for selected version-pruning candidates.
- **FR-002**: Pruning application MUST permanently remove only explicit candidate resource versions selected by the host.
- **FR-003**: Pruning application MUST NOT run automatically, schedule work, discover candidates implicitly, or apply all preview results without host selection.
- **FR-004**: Pruning application MUST require candidates to identify the policy ID, policy kind, prune-preview outcome, resource identity, and resource version from the preview candidate.
- **FR-005**: Pruning application MUST resolve exactly one effective tenant per request.
- **FR-006**: Omitted tenant scope MUST continue to mean the documented default single-tenant scope.
- **FR-007**: Pruning application MUST NOT remove versions outside the effective tenant boundary.
- **FR-008**: Pruning application MUST re-check current resource existence, version existence, latest status, activation state, lifecycle marker state when the current policy criteria require it, policy declaration compatibility, policy criteria compatibility, and retained-version safety before removing a candidate version.
- **FR-009**: Pruning application MUST fail closed when a candidate is stale, malformed, unsupported, outside the effective tenant, currently latest, currently active, no longer matching current policy criteria, or unsafe to prune.
- **FR-010**: Pruning application MUST NOT remove every retained version for a resource unless a future feature explicitly defines that behavior.
- **FR-011**: Pruning application MUST allow partial success for unrelated candidates while still returning exactly one result for every input candidate.
- **FR-012**: Pruning application MUST report stable per-candidate statuses for pruned, already-pruned, skipped duplicate, and failed outcomes.
- **FR-013**: Pruning diagnostics MUST be stable enough for hosts and tests to distinguish invalid candidate shape, stale preview basis, missing target, tenant-scoped target-not-found behavior, protected latest version, protected active version, policy criteria mismatch, unsafe retained-version removal, provider unsupported behavior, and provider write failure.
- **FR-014**: Duplicate candidates in one request MUST be handled deterministically.
- **FR-015**: Reapplying a candidate for a version that was already removed MUST produce an idempotent already-pruned outcome when the resource still exists and the candidate is otherwise valid for the effective tenant.
- **FR-016**: Pruning application MUST NOT rewrite remaining resource versions, mutate activation state, mutate lifecycle marker state, mutate policy declarations, or change portability snapshot semantics beyond the absence of pruned versions.
- **FR-017**: Pruning application MUST keep archive and soft-delete restore workflows separate from destructive version pruning.
- **FR-018**: Providers that cannot support destructive version removal MUST fail closed with a stable unsupported diagnostic rather than silently ignoring pruning candidates.
- **FR-019**: Providers that support destructive version removal SHOULD remove each valid candidate conditionally using current-state checks; cross-candidate all-or-nothing behavior is not required, and results MUST identify each candidate outcome.
- **FR-020**: Documentation MUST explain host-controlled candidate selection, destructive behavior, safety preflight, tenant boundaries, idempotency, partial success, provider unsupported behavior, and out-of-scope automatic execution.
- **FR-021**: The feature MUST NOT introduce background schedulers, hidden retention jobs, authorization or permission policy engines, cross-tenant pruning, runtime scanning, provider registries, public SQL, public queryable resource surfaces, broad workflow/state-machine infrastructure, or schema migrations.

### Key Entities *(include if feature involves data)*

- **Pruning Application Request**: Host-provided request containing the effective tenant scope, selected pruning candidates, and optional host metadata for audit or reporting.
- **Pruning Candidate**: A selected version-pruning target containing resource identity, version identity, expected version number, and preview basis used for stale-selection checks.
- **Pruning Application Result**: Ordered response containing the effective tenant and one candidate result for every submitted candidate.
- **Pruning Candidate Result**: Per-candidate outcome with status, target identity, and diagnostics when the candidate fails or cannot be applied.
- **Pruning Diagnostic**: Stable diagnostic describing invalid input, stale preview basis, missing target, protected state, unsafe retained-version removal, unsupported provider behavior, or write failure.
- **Provider Pruning Capability**: Provider-declared ability to permanently remove resource versions while preserving tenant boundaries and remaining resource consistency.

### Assumptions

- Hosts already use pruning previews from policy evaluation to decide which candidates to submit.
- This slice applies only to version-pruning outcomes, not archive, soft-delete, restore, resource deletion, or definition deletion.
- The default single-tenant behavior remains valid for callers that omit tenant scope.
- Authorization decisions remain host responsibilities outside this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can apply a selected subset of pruning preview candidates and verify that 100% of unselected versions remain.
- **SC-002**: Stale, latest, active, policy-mismatched, missing, malformed, and tenant-mismatched candidates produce stable failed results without removing protected versions.
- **SC-003**: Mixed requests return exactly one ordered result for every submitted candidate, including partial success cases.
- **SC-004**: Reapplying an already-pruned candidate produces an idempotent outcome without removing additional versions.
- **SC-005**: Tenant-scoped pruning removes zero versions outside the effective tenant boundary.
- **SC-006**: Existing archive, soft-delete, restore, activation, query, portability, and default single-tenant workflows remain behaviorally unchanged.
