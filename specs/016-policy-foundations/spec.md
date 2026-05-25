# Feature Specification: Policy Foundations

**Feature Branch**: `016-policy-foundations`  
**Created**: 2026-05-25  
**Status**: Draft  
**Input**: User description: "Introduce explicit policy contracts for retention, archival, soft-delete, and version pruning while preserving append-only resource history by default. Include policy metadata models and validation diagnostics, explicit policy evaluation entry points for hosts and providers, soft-delete/archive markers that remain queryable through declared scopes, version pruning previews before destructive writes, and keep background schedulers, hidden retention jobs, authorization policy engines, runtime scanning, provider registries, public SQL, and IQueryable<Resource> out of scope."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Declare Resource Policies Explicitly (Priority: P1)

As a host author, I need to declare retention, archival, soft-delete, and pruning intent as explicit resource policy metadata, so policy behavior is discoverable before any host decides to act on it.

**Why this priority**: Policy declarations are the foundation for every later policy workflow. Without explicit declarations, previews, diagnostics, and host execution decisions would be ambiguous.

**Independent Test**: Declare policy metadata for a resource type and verify the declaration can be stored, inspected, validated, and reported without changing any resource history.

**Acceptance Scenarios**:

1. **Given** a resource type has no policy metadata, **When** a host inspects policy information, **Then** the system reports that no policy is declared and leaves resource history unchanged.
2. **Given** a host declares retention, archival, soft-delete, or pruning intent, **When** the declaration is inspected, **Then** the declared policy name, scope, target, and effective settings are visible to host code.
3. **Given** a policy declaration is incomplete, contradictory, or unsupported, **When** the host validates it, **Then** the system reports stable diagnostics and does not apply any policy outcome.
4. **Given** existing resources were created before policies exist, **When** policy metadata is introduced, **Then** existing resource versions remain append-only and are not modified automatically.

---

### User Story 2 - Preview Policy Outcomes Before Action (Priority: P2)

As a host author, I need to evaluate declared policies and receive a preview of candidate outcomes, so I can decide whether to archive, soft-delete, or prune through an explicit host-controlled workflow.

**Why this priority**: Retention and pruning are potentially destructive or compliance-sensitive. The first slice must prove deterministic preview behavior before any write action exists.

**Independent Test**: Create resources and versions that match declared policy conditions, run policy evaluation in preview mode, and verify the preview identifies candidate outcomes without changing stored resources.

**Acceptance Scenarios**:

1. **Given** resources match an archival policy, **When** the host evaluates policies, **Then** the preview lists the resources that would be archived and explains the matching policy.
2. **Given** resource versions match a pruning policy, **When** the host evaluates policies, **Then** the preview lists candidate versions and the reason each version is eligible.
3. **Given** a policy evaluation encounters unsupported or invalid policy metadata, **When** the host requests a preview, **Then** the preview includes diagnostics and no write-side outcome occurs.
4. **Given** a tenant-scoped host evaluates policies for one tenant, **When** similar resources exist in another tenant, **Then** the preview includes only resources within the effective tenant boundary.

---

### User Story 3 - Mark And Query Resource Lifecycle State (Priority: P3)

As a host author, I need soft-delete and archive outcomes to be explicit lifecycle markers that remain queryable, so hosts can build clear restore, audit, and administrative workflows without hidden filtering rules.

**Why this priority**: Policy outcomes must be visible and reversible where appropriate. Hidden default filtering would create surprising behavior and make audits harder.

**Independent Test**: Mark resources as archived or soft-deleted, then verify normal resource history remains intact and hosts can query or inspect the lifecycle state through explicit criteria.

**Acceptance Scenarios**:

1. **Given** a resource is soft-deleted, **When** the host reads or queries with criteria that include lifecycle state, **Then** the resource can be found with its soft-delete marker and original versions intact.
2. **Given** a resource is archived, **When** the host inspects resource lifecycle state, **Then** the archive marker is visible without removing activation, version, or portability history.
3. **Given** a host does not ask for lifecycle-state filtering, **When** it uses existing resource workflows, **Then** policy markers do not introduce hidden background behavior or implicit authorization decisions.

### Edge Cases

- A policy declaration references an unknown target, unsupported outcome, invalid duration, or contradictory settings.
- Multiple policy declarations apply to the same resource or version and produce overlapping candidate outcomes.
- A policy preview includes resources with no active version, multiple active versions, archived state, or soft-deleted state.
- A pruning preview would include every version of a resource, including the latest or currently active version.
- Policy metadata is present on data exported from one tenant and imported into another tenant.
- A host evaluates policies with omitted tenant scope after tenant-scoped data exists.
- A host attempts to apply a policy outcome when only preview behavior is supported in this slice.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The simplest acceptable behavior is explicit policy declarations, validation diagnostics, preview-only evaluation, and visible lifecycle markers. Automatic execution, schedulers, hidden retention jobs, authorization policy engines, cross-tenant policy workflows, runtime scanning, provider registries, public SQL, and public `IQueryable<Resource>` are out of scope.
- **Explicitness**: Policy declarations, previews, diagnostics, and lifecycle markers must be visible through host-controlled inputs and outputs. The system must not discover or execute policies through hidden conventions, ambient runtime state, or background processing.
- **Dependencies**: None.
- **Operational Impact**: Existing local development, deployment, and debugging remain unchanged. Hosts opt in to policy declaration and preview workflows explicitly; no background workers, timers, external services, or deployment-time policy runner are introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support explicit policy declarations for retention, archival, soft-delete, and version pruning intent.
- **FR-002**: Policy declarations MUST identify their policy kind, target scope, outcome intent, and host-visible settings in a way that can be inspected before evaluation.
- **FR-003**: The system MUST validate policy declarations and report stable diagnostics for missing, invalid, contradictory, or unsupported policy metadata.
- **FR-004**: Policy validation MUST NOT mutate resources, versions, activation state, lifecycle markers, definitions, or portable snapshots.
- **FR-005**: Hosts MUST be able to request a policy evaluation preview for a bounded scope of resources.
- **FR-006**: Policy evaluation previews MUST report candidate outcomes, matching policy declarations, affected resource identifiers, affected version identifiers when applicable, and diagnostics when evaluation cannot proceed.
- **FR-007**: Policy evaluation previews MUST NOT archive, soft-delete, prune, deactivate, delete, or otherwise mutate resource data.
- **FR-008**: Version pruning behavior in this slice MUST be preview-only and MUST NOT perform destructive writes.
- **FR-009**: The system MUST prevent or diagnose pruning previews that would remove all retained versions of a resource unless a future feature explicitly defines that behavior.
- **FR-010**: Soft-delete and archive outcomes MUST be represented as explicit lifecycle markers rather than physical deletion.
- **FR-011**: Lifecycle markers MUST preserve append-only resource history and MUST NOT rewrite prior resource versions.
- **FR-012**: Hosts MUST be able to inspect and query resources by lifecycle marker state through explicit criteria.
- **FR-013**: Existing resource reads, writes, activation behavior, schema upgrades, portability, and lifecycle hooks MUST remain deterministic when policy metadata exists.
- **FR-014**: Tenant-scoped policy validation, preview, lifecycle markers, and query behavior MUST operate within one effective tenant boundary.
- **FR-015**: Omitted tenant scope MUST continue to mean the documented default single-tenant scope.
- **FR-016**: Portable export and import workflows MUST preserve policy declarations and lifecycle markers when they are included in the selected resource scope.
- **FR-017**: Policy diagnostics MUST be stable enough for hosts and tests to distinguish invalid declaration, unsupported policy kind, unsupported outcome, invalid target, conflicting declaration, and preview-only enforcement failures.
- **FR-018**: The feature MUST NOT introduce background schedulers, hidden retention jobs, automatic policy execution, authorization or permission policy engines, cross-tenant policy evaluation, runtime scanning, provider registries, public SQL, or public `IQueryable<Resource>`.
- **FR-019**: Documentation MUST explain explicit policy declaration, validation, preview-only evaluation, lifecycle marker visibility, tenant boundary behavior, portability behavior, and out-of-scope automatic execution.

### Key Entities

- **Policy Declaration**: Host-visible metadata describing a policy kind, target scope, outcome intent, and settings. It is declarative and does not execute by itself.
- **Policy Target Scope**: The bounded set of definitions, resources, versions, tenants, lifecycle states, or activation states a policy declaration is intended to evaluate against.
- **Policy Evaluation Preview**: A host-requested report that identifies candidate outcomes and diagnostics without mutating data.
- **Policy Candidate Outcome**: A preview item describing a resource or version that would be archived, soft-deleted, retained, or considered for pruning if a future explicit write workflow applies the policy.
- **Lifecycle Marker**: An explicit resource lifecycle state such as archived or soft-deleted. It remains visible for reads, queries, audits, portability, and future restore flows.
- **Policy Diagnostic**: A stable validation or preview message describing invalid metadata, unsupported behavior, conflicting declarations, unsafe pruning, or preview-only enforcement.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can declare each supported policy kind and inspect the declaration without any resource data changing.
- **SC-002**: Invalid or unsupported policy declarations produce stable diagnostics that automated tests can assert.
- **SC-003**: A host can run a policy preview over resources with matching and non-matching data and receive candidate outcomes with no mutations.
- **SC-004**: A pruning preview can identify eligible versions while preserving all stored resource versions and activation state.
- **SC-005**: Archived and soft-deleted resources remain discoverable through explicit lifecycle-state criteria.
- **SC-006**: Tenant-scoped policy previews and lifecycle marker queries return zero resources from outside the effective tenant boundary.
- **SC-007**: Existing single-tenant tests and workflows continue to pass without requiring policy declarations.

## Assumptions

- This slice defines policy foundations and preview behavior, not automatic policy execution.
- Hosts remain responsible for deciding when and whether to apply candidate outcomes.
- Authorization, permissions, legal compliance interpretation, and scheduling are host responsibilities.
- Destructive pruning writes, restore workflows, and automatic retention jobs require future specifications.
- Lifecycle markers are additive state and do not rewrite historical resource versions.
