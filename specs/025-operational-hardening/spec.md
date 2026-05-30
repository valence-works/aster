# Feature Specification: Operational Hardening

**Feature Branch**: `025-operational-hardening`
**Created**: 2026-05-30
**Status**: Draft
**Input**: Proceed with housekeeping, then the next recommended bounded candidate: operational hardening.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Retry Restore Safely (Priority: P1)

As a host developer, I want lifecycle restore application to remain deterministic when the same selected restore candidate is retried or raced so that operational retries do not recreate markers or produce ambiguous state.

**Why this priority**: Restore is a host-controlled recovery workflow and should remain safe under common retry behavior.

**Independent Test**: Apply a marker, restore it, retry the same restore, and run two same-candidate restores concurrently; verify terminal statuses and final marker state.

**Acceptance Scenarios**:

1. **Given** an archived resource marker, **When** restore is applied twice with the same candidate, **Then** the first result reports restored, the retry reports already restored, and the marker remains cleared.
2. **Given** an archived resource marker, **When** two identical restore applications run concurrently, **Then** exactly one result restores, the other observes already-restored or equivalent safe terminal state, and the marker remains cleared.

---

### User Story 2 - Retry Pruning Safely (Priority: P2)

As a host developer, I want version pruning application to be retry-safe so that destructive maintenance jobs can retry the same selected target without deleting additional versions.

**Why this priority**: Pruning is destructive and already has safety preflight; retry behavior should be pinned down.

**Independent Test**: Prune one eligible historical draft version, retry the same candidate, and verify only the selected version is removed.

**Acceptance Scenarios**:

1. **Given** a resource with an eligible historical draft version, **When** pruning is applied then retried for the same target, **Then** the first result reports pruned, the retry reports already pruned, and remaining versions are unchanged by the retry.
2. **Given** SQLite-backed resources, **When** pruning is retried after reopening the provider, **Then** persisted state reports already pruned and does not remove additional rows.

---

### User Story 3 - Repeat Historical Activation Predictably (Priority: P3)

As a host developer, I want repeated historical activation requests to be deterministic so that retrying activation does not duplicate active versions or change latest version state.

**Why this priority**: Historical activation is a core advanced-versioning operation and should remain stable under host retries.

**Independent Test**: Activate the same historical version repeatedly in single-active and multi-active modes and verify active channels contain deterministic unique versions while latest remains unchanged.

**Acceptance Scenarios**:

1. **Given** a resource with two versions, **When** the same historical version is activated twice in single-active mode, **Then** the active channel contains that version once and latest remains the highest saved version.
2. **Given** a resource with multiple versions, **When** the same historical version is activated twice with multi-active enabled, **Then** the active channel contains unique ordered versions and latest remains unchanged.

### Edge Cases

- Hardening MUST focus on targeted regression coverage before product changes.
- Tests MUST be deterministic and bounded to explicit resources and tenants.
- Retry and concurrency-sensitive tests MUST assert final persisted state, not only result statuses.
- If a hardening test exposes a bug, the fix MUST remain narrowly scoped to existing behavior.
- The slice MUST NOT introduce new public product behavior, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, schedulers, background jobs, benchmark infrastructure, or new dependencies.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add focused regression coverage and only fix bugs exposed by that coverage.
- **Explicitness**: Tests use explicit resources, tenants, candidates, and channels.
- **Dependencies**: None.
- **Operational Impact**: No runtime setup, storage, deployment, or observability changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST include retry coverage for lifecycle restore application over existing in-memory behavior.
- **FR-002**: The system MUST include concurrency-sensitive same-candidate restore coverage that verifies safe terminal statuses and final marker state.
- **FR-003**: The system MUST include retry coverage for policy pruning application over existing in-memory behavior.
- **FR-004**: The system MUST include persisted SQLite retry coverage for policy pruning application.
- **FR-005**: The system MUST include repeated historical activation coverage for single-active channels.
- **FR-006**: The system MUST include repeated historical activation coverage for multi-active channels.
- **FR-007**: The system MUST verify final resource, marker, activation, or version state after each hardening scenario.
- **FR-008**: The system MUST update roadmap housekeeping so `024-version-history-summaries` is listed as landed and `025-operational-hardening` is the active slice.
- **FR-009**: The system MUST NOT introduce new product APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, schedulers, benchmark infrastructure, or dependencies.

### Key Entities

- **Restore Retry Scenario**: Existing restore candidate applied more than once or concurrently.
- **Pruning Retry Scenario**: Existing pruning candidate applied more than once.
- **Historical Activation Retry Scenario**: Existing version activation repeated for the same channel.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Restore retry tests prove final lifecycle marker state is cleared after repeated and concurrent same-candidate restore attempts.
- **SC-002**: Pruning retry tests prove only the originally selected resource version is removed and retries report already-pruned behavior.
- **SC-003**: Historical activation retry tests prove active version lists remain unique and ordered while latest version identity is unchanged.
- **SC-004**: The full test suite passes without introducing new dependencies, storage migrations, or product APIs.
