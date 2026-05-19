# Feature Specification: Definition Schema Versions & Upgrade Flow

**Feature Branch**: `012-definition-schema-upgrades`  
**Created**: 2026-05-18  
**Status**: Draft  
**Input**: User description: "Make the definition-version story explicit enough for long-lived resources. Model resource definition version references on resources, define upgrade behavior from older definition versions, and add validation/tests for resources that span schema versions. Keep the slice small: no migrations, no automatic data rewriting, no planner, no runtime scanning, and no provider registry."

## Clarifications

### Session 2026-05-19

- Q: During upgrade, what happens to source aspect data that is not declared by the target definition version? → A: Preserve all existing aspect data by default, even if the target definition no longer declares that aspect; report it as carried-forward data.
- Q: Which resource version can be used as the source for an explicit schema upgrade? → A: Upgrade only from the latest resource version; stale base versions fail with existing optimistic concurrency behavior.
- Q: How should resources without recorded definition version lineage be classified? → A: Missing lineage remains unknown until an explicit upgrade creates a new version with current lineage.
- Q: Which definition version may an upgrade target? → A: Upgrades default to latest, but callers may explicitly target any existing definition version newer than the source lineage and not newer than latest.
- Q: What scope does schema status evaluate? → A: Schema status is evaluated for one resource version at a time.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preserve Definition Version Lineage (Priority: P1)

As an SDK user managing long-lived resources, I want each resource version to identify the resource definition version it was created against so historical resources remain understandable after definitions evolve.

**Why this priority**: Without explicit lineage, hosts cannot safely explain or validate resource versions that outlive their original schema.

**Independent Test**: Register a definition, create a resource, register a newer definition version, create or update resources again, and verify each resource version reports the correct definition version without changing historical versions.

**Acceptance Scenarios**:

1. **Given** a resource definition at version 1, **When** a resource is created, **Then** the created resource version records definition version 1.
2. **Given** a resource created against definition version 1 and a later definition version 2, **When** the original resource is read, **Then** it still reports definition version 1.
3. **Given** a definition has advanced to version 2, **When** a new resource of that definition is created, **Then** the new resource records definition version 2.

---

### User Story 2 - Detect Resources Behind The Latest Definition (Priority: P2)

As a host application, I want to determine whether a resource version is current, behind the latest definition, or references a missing definition version so I can surface safe upgrade prompts and diagnostics.

**Why this priority**: Lineage is useful only if callers can compare it against available definitions and receive structured status instead of inferring from raw numbers.

**Independent Test**: Create resource versions against multiple definition versions, omit a referenced definition version in test setup, and verify per-version status results distinguish current, upgradeable, and invalid lineage.

**Acceptance Scenarios**:

1. **Given** a resource version that references the latest definition version, **When** schema status is checked, **Then** the status is current.
2. **Given** a resource version that references an older available definition version, **When** schema status is checked, **Then** the status reports that a newer definition version is available.
3. **Given** a resource version that references a definition version that cannot be found, **When** schema status is checked, **Then** the status reports a missing definition version with enough information for troubleshooting.
4. **Given** a resource has multiple versions with different definition lineage, **When** schema status is checked for one version, **Then** the result describes only that requested resource version.

---

### User Story 3 - Upgrade A Resource Explicitly (Priority: P3)

As an SDK user, I want to upgrade a resource to the latest compatible definition through an explicit operation that creates a new immutable resource version, preserving existing aspect data unless the caller intentionally changes it.

**Why this priority**: Upgrade behavior must be deterministic before providers, hosts, or future portability features depend on schema evolution.

**Independent Test**: Create a resource against an older definition version, register a newer compatible definition version, run the upgrade operation, and verify a new resource version is created with the target definition version while the prior version remains unchanged.

**Acceptance Scenarios**:

1. **Given** a resource version that is behind the latest compatible definition, **When** the caller upgrades it, **Then** a new resource version is appended with the latest definition version.
2. **Given** an upgraded resource version, **When** its aspects are inspected, **Then** existing aspect data is preserved unless the caller supplied explicit updates.
3. **Given** a resource already using the target definition version, **When** the caller requests an upgrade to that same version, **Then** no duplicate version is created and the caller receives a clear no-op result.
4. **Given** the target definition version no longer declares an aspect that exists on the source resource version, **When** the caller upgrades without explicit data changes, **Then** that aspect data is preserved and reported as carried-forward data.
5. **Given** a caller attempts to upgrade from a non-latest resource version, **When** a newer resource version already exists, **Then** the upgrade fails through existing optimistic concurrency behavior.
6. **Given** a resource version using definition version 1 while versions 2 and 3 exist, **When** the caller explicitly targets definition version 2, **Then** the new resource version records definition version 2.

---

### Edge Cases

- A resource has no recorded definition version because it was created before lineage became required; schema status reports unknown lineage until an explicit upgrade creates a new version with current lineage.
- A resource references a definition identifier that does not exist.
- A resource references a definition version that is older than the latest but still available.
- A resource references a definition version number greater than the latest known definition version.
- A caller targets an existing definition version older than or equal to the source lineage.
- A caller targets a definition version newer than the latest known definition version.
- A definition changes singleton status between versions while resources already exist.
- An upgrade target removes an aspect key that exists on the source resource version; the upgrade preserves that data by default and reports it as carried-forward data.
- A resource is upgraded while another update has already produced a newer resource version.
- A caller attempts to upgrade a historical resource version rather than the latest resource version.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The slice defines lineage, status, and explicit append-only upgrades only. It intentionally excludes migrations, automatic data rewriting, transformation pipelines, planner behavior, runtime scanning, and provider registries.
- **Explicitness**: Callers choose when to inspect schema status and when to upgrade. No resource is silently rewritten because a definition changed.
- **Dependencies**: None.
- **Operational Impact**: Existing local development, testing, persistence setup, and debugging workflows remain unchanged. The feature adds observable schema-evolution behavior without new infrastructure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Resource versions MUST record the resource definition version they were created against.
- **FR-002**: Existing historical resource versions MUST retain their recorded definition version when newer definition versions are registered.
- **FR-003**: Creating a new resource MUST use the latest available definition version for its definition identifier.
- **FR-004**: Updating an existing resource without an explicit schema upgrade MUST preserve the base resource version's recorded definition version.
- **FR-005**: The system MUST provide a way to inspect one resource version's schema status relative to registered definition versions.
- **FR-006**: Schema status MUST distinguish at least current, older-than-latest, missing-definition, missing-definition-version, and unknown-resource-lineage states.
- **FR-007**: Schema status MUST include the resource's recorded definition identifier, recorded definition version when known, and latest available definition version when known.
- **FR-008**: The system MUST provide an explicit upgrade action that can move a resource to a target definition version by creating a new resource version.
- **FR-009**: The default upgrade target SHOULD be the latest available definition version for the resource's definition identifier.
- **FR-010**: Upgrade actions MUST NOT mutate or rewrite previous resource versions.
- **FR-011**: Upgrade actions MUST preserve existing aspect data by default.
- **FR-012**: Upgrade actions MUST allow callers to supply explicit aspect changes as part of the new upgraded resource version.
- **FR-013**: Upgrade actions MUST detect a request to upgrade to the same definition version and return a clear no-op result rather than appending a duplicate version.
- **FR-014**: Upgrade actions MUST fail with structured diagnostics when the target definition version does not exist.
- **FR-015**: Upgrade actions MUST respect existing optimistic concurrency behavior for resource updates.
- **FR-016**: Singleton resource rules MUST remain enforceable across definition versions.
- **FR-017**: Query and read behavior MUST preserve resources spanning multiple definition versions without requiring automatic upgrade.
- **FR-018**: The feature MUST NOT introduce automatic migrations, background rewriting, runtime scanning, provider registries, query planning, public SQL, or public `IQueryable<Resource>`.
- **FR-019**: Upgrade actions MUST preserve source aspect data that is not declared by the target definition version unless the caller explicitly changes it, and MUST report preserved undeclared data as carried-forward data.
- **FR-020**: Upgrade actions MUST use the latest resource version as their source and MUST fail through existing optimistic concurrency behavior when the caller's base version is stale.
- **FR-021**: Resource versions without recorded definition version lineage MUST be classified as unknown-resource-lineage and MUST NOT be treated as the latest or earliest definition version by assumption.
- **FR-022**: Explicit upgrade of an unknown-lineage resource MUST create a new resource version with current target definition lineage when the target definition version exists and concurrency checks pass.
- **FR-023**: Upgrade actions SHOULD allow callers to explicitly target any existing definition version newer than the source lineage and not newer than the latest known definition version.
- **FR-024**: Upgrade actions MUST reject target definition versions that are older than or equal to the source lineage, unless the target equals the source lineage and the operation returns the no-op result described by FR-013.
- **FR-025**: Upgrade actions MUST reject target definition versions newer than the latest known definition version.
- **FR-026**: Schema status MUST be evaluated for a single requested resource version and MUST NOT summarize all versions of a resource in this slice.

### Key Entities *(include if feature involves data)*

- **Resource Definition Version**: An immutable schema snapshot for a resource definition, identified by a stable definition identifier and a version number.
- **Resource Version Lineage**: The recorded link from a resource version to the definition version that governed its shape at creation time.
- **Schema Status**: A diagnostic result that compares one resource version's lineage with currently registered definition versions.
- **Upgrade Request**: A caller-initiated command to append a new resource version using a selected target definition version.
- **Upgrade Result**: The outcome of an upgrade request, including whether a new version was created, no action was needed, or the request failed validation.
- **Carried-Forward Data**: Existing resource aspect data preserved during upgrade even though the target definition version does not declare that aspect.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller can create resources before and after a definition update and observe the correct definition version lineage for every resource version.
- **SC-002**: A caller can classify at least five representative resource versions as current, older-than-latest, missing-definition, missing-definition-version, or unknown-lineage without reading provider-specific storage.
- **SC-003**: A caller can explicitly upgrade an older resource and verify that exactly one new resource version is created while prior versions remain unchanged.
- **SC-004**: A caller can run existing query/read flows over resources spanning multiple definition versions without first upgrading those resources.
- **SC-005**: Existing provider-backed and in-memory resource lifecycle tests continue to pass without new infrastructure or data migration steps.
