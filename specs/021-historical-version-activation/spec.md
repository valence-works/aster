# Feature Specification: Historical Version Activation

**Feature Branch**: `021-historical-version-activation`
**Created**: 2026-05-29
**Status**: Draft
**Input**: User description: "Allow hosts to explicitly activate historical resource versions in activation channels. Activating an older existing version should no longer be treated as a concurrency conflict; latest remains unchanged, resource versions remain immutable, activation state remains separate, tenant boundaries remain explicit, and single-active versus multi-active channel behavior remains controlled by the existing allowMultipleActive flag. Keep the slice bounded: no schema changes, no new provider registry, no scheduler, no authorization engine, no public SQL, no public IQueryable<Resource>, no broad workflow engine, and no mutation beyond activation state."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Activate Historical Version (Priority: P1)

As a host author, I need to activate an older existing resource version in a channel, so that hosts can promote or roll back to a known historical snapshot without creating a new resource version.

**Why this priority**: The roadmap names historical promotion as part of the activation model, and version history inspection now makes historical versions visible to hosts.

**Independent Test**: Create a resource with multiple versions, activate version 1 after version 2 exists, and verify version 1 becomes active while version 2 remains latest.

**Acceptance Scenarios**:

1. **Given** a resource has versions 1 and 2, **When** the host activates version 1 in `Published`, **Then** version 1 is active in that channel and version 2 remains latest.
2. **Given** a historical version is activated with single-active behavior, **When** the channel previously had another active version, **Then** the channel contains only the requested historical version.
3. **Given** a historical version is activated with multi-active behavior, **When** the channel previously had another active version, **Then** both active versions remain active in deterministic order.

---

### User Story 2 - Preserve Safety and Existing Boundaries (Priority: P2)

As a host author, I need historical activation to preserve existing validation, tenant scoping, lifecycle hooks, and activation storage behavior, so that the feature is a safe extension of current activation semantics.

**Why this priority**: Activation remains a state transition with lifecycle hooks and tenant boundaries; broadening the allowed version set must not bypass existing protections.

**Independent Test**: Attempt to activate a missing version, activate matching identifiers in separate tenants, and verify hook contexts contain the requested historical version and resulting active set.

**Acceptance Scenarios**:

1. **Given** a requested historical version does not exist, **When** activation runs, **Then** activation fails with the existing version-not-found behavior.
2. **Given** matching resource identifiers exist in two tenants, **When** a historical version is activated in tenant A, **Then** tenant B activation state is unchanged.
3. **Given** lifecycle hooks are registered, **When** a historical version is activated, **Then** hooks receive the requested version and resulting active version list.

### Edge Cases

- Requested resource identifier is null, empty, or whitespace.
- Requested channel is null, empty, or whitespace.
- Requested version exists but is not latest.
- Requested version is already active in the channel.
- Activation runs with `allowMultipleActive` set to false.
- Activation runs with `allowMultipleActive` set to true.
- Matching resource IDs exist in other tenants.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature SHOULD update existing activation semantics directly rather than adding a new promotion service or workflow engine.
- **Explicitness**: Hosts explicitly pass the resource ID, version, channel, tenant scope, and `allowMultipleActive` flag through existing activation APIs.
- **Dependencies**: None.
- **Operational Impact**: No storage schema change, migration, background process, provider registry, or deployment change is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow activation of any existing resource version, including historical non-latest versions.
- **FR-002**: Historical activation MUST NOT create a new resource version or mutate any resource version payload.
- **FR-003**: Historical activation MUST NOT change which version is latest.
- **FR-004**: Historical activation MUST continue to write only activation state.
- **FR-005**: Historical activation MUST preserve existing `allowMultipleActive = false` behavior by replacing the channel's active versions with the requested version.
- **FR-006**: Historical activation MUST preserve existing `allowMultipleActive = true` behavior by adding the requested version alongside existing active versions.
- **FR-007**: Resulting active version lists MUST remain deterministic and ordered.
- **FR-008**: Activating a missing version MUST fail with existing version-not-found behavior.
- **FR-009**: Historical activation MUST resolve exactly one effective tenant per request.
- **FR-010**: Historical activation MUST NOT read or mutate activation state outside the effective tenant.
- **FR-011**: Lifecycle activation hooks MUST continue to run for historical activation with the requested version and resulting active version list.
- **FR-012**: Documentation MUST explain historical activation, latest-version preservation, single-active versus multi-active behavior, tenant boundaries, and non-goals.
- **FR-013**: The feature MUST NOT introduce schema changes, provider registries, runtime scanning, schedulers, authorization engines, public SQL, public `IQueryable<Resource>`, or broad workflow/state-machine infrastructure.

### Key Entities *(include if feature involves data)*

- **Historical Version Activation**: Existing activation operation targeting a non-latest but existing resource version.
- **Activation Channel**: Host-defined channel whose active version set is updated.
- **Active Version Set**: Ordered list of active resource version numbers stored separately from resource payloads.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can activate version 1 after version 2 exists and observe version 1 active while version 2 remains latest.
- **SC-002**: Single-active and multi-active historical activation scenarios are both covered by tests.
- **SC-003**: Tenant isolation tests show historical activation in one tenant does not affect matching identifiers in another tenant.
- **SC-004**: Existing activation, lifecycle, query, policy, portability, and SQLite tests continue to pass unchanged except for tests updated to reflect the new supported historical activation behavior.
