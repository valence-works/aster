# Feature Specification: Lifecycle Restore Summaries

**Feature Branch**: `026-lifecycle-restore-summaries`
**Created**: 2026-05-30
**Status**: Draft
**Input**: Continue with the next bounded Phase 5 slice after operational hardening.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Restore Application Results (Priority: P1)

As a host developer rendering restore operation feedback, I want a deterministic summary of lifecycle restore application results so that I can show how many resources were restored, already restored, skipped, or failed without recomputing counts in every host.

**Why this priority**: Restore application is the write-side operator workflow. It needs the same lightweight host-facing reporting convenience that policy application and pruning application already have.

**Independent Test**: Construct a lifecycle restore application result with restored, already-restored, skipped, failed, duplicate resource identifiers, and diagnostics, then verify aggregate counts and distinct affected resources.

**Acceptance Scenarios**:

1. **Given** a restore application result with mixed candidate statuses, **When** it is summarized, **Then** the summary reports total, restored, already-restored, skipped, and failed counts.
2. **Given** successful restore candidates reference repeated resource identifiers, **When** the application result is summarized, **Then** affected resource count is distinct by ordinal resource identifier.
3. **Given** failed candidates contain stable diagnostics, **When** the result is summarized, **Then** diagnostic code counts are deterministic and ordered.

---

### User Story 2 - Summarize Restore Preview Results (Priority: P2)

As a host developer rendering a restore preview screen, I want a deterministic summary of lifecycle restore preview results so that I can show how many selected candidates are restorable before any marker state is changed.

**Why this priority**: Preview is the operator review step before writes. A pure summary makes preview screens and tests simpler while preserving non-mutating behavior.

**Independent Test**: Construct a lifecycle restore preview result with restorable, already-restored, skipped, failed, duplicate resource identifiers, and diagnostics, then verify aggregate counts and distinct candidate resources.

**Acceptance Scenarios**:

1. **Given** a restore preview result with mixed candidate statuses, **When** it is summarized, **Then** the summary reports total, restorable, already-restored, skipped, and failed counts.
2. **Given** preview candidates contain repeated resource identifiers, **When** the preview result is summarized, **Then** candidate resource count is distinct by ordinal resource identifier.
3. **Given** failed preview candidates contain diagnostics, **When** the result is summarized, **Then** diagnostic code counts are deterministic and ordered.

---

### User Story 3 - Keep Restore Summaries Pure and Predictable (Priority: P3)

As a host developer, I want restore summaries to be pure transformations over existing result objects so that they are safe to use in UI adapters, tests, logs, and reports without service resolution or provider state.

**Why this priority**: The feature should not change restore execution. Its value is a deletable, explicit convenience over already-returned result objects.

**Independent Test**: Construct restore result objects manually and summarize them without any registered services or storage provider.

**Acceptance Scenarios**:

1. **Given** manually constructed restore result objects, **When** summary helpers are called, **Then** summaries are produced without services, stores, providers, or mutations.
2. **Given** null restore result input, **When** summary helpers are called, **Then** they fail fast with argument validation.
3. **Given** null candidate collections on result objects, **When** summary helpers are called, **Then** they are treated as empty collections.

### Edge Cases

- Null restore result inputs MUST fail fast with argument validation.
- Null candidate collections MUST be treated as empty collections.
- Blank resource identifiers MUST be ignored for distinct resource counts.
- Blank diagnostic codes MUST be ignored for diagnostic code counts.
- Diagnostic code counts MUST be ordered deterministically by ordinal code.
- Application success MUST count restored and already-restored candidates as successful terminal statuses.
- Preview success MUST count restorable and already-restored candidates as successful terminal statuses.
- Summaries MUST NOT perform reads, writes, validation, policy evaluation, lifecycle marker changes, storage access, provider access, background work, or query planning.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add pure extension helpers and records over existing restore result models only. No service, registry, provider, workflow, or audit store is introduced.
- **Explicitness**: Callers explicitly summarize existing result objects. There is no hidden discovery, scanning, ambient state, or automatic reporting behavior.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, provider setup, debugging, observability, or runtime behavior changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a pure summary for lifecycle restore application results.
- **FR-002**: A restore application summary MUST preserve the effective tenant and optional restored timestamp from the source result.
- **FR-003**: A restore application summary MUST report total, restored, already-restored, skipped, and failed candidate counts.
- **FR-004**: A restore application summary MUST expose whether any candidates failed.
- **FR-005**: A restore application summary MUST expose whether every candidate completed through a successful terminal application status.
- **FR-006**: A restore application summary MUST report the number of distinct nonblank resource identifiers affected by restored or already-restored candidates.
- **FR-007**: The system MUST provide a pure summary for lifecycle restore preview results.
- **FR-008**: A restore preview summary MUST preserve the effective tenant from the source result.
- **FR-009**: A restore preview summary MUST report total, restorable, already-restored, skipped, and failed candidate counts.
- **FR-010**: A restore preview summary MUST expose whether any candidates failed.
- **FR-011**: A restore preview summary MUST expose whether every candidate completed through a successful terminal preview status.
- **FR-012**: A restore preview summary MUST report the number of distinct nonblank resource identifiers represented by restorable or already-restored candidates.
- **FR-013**: Restore summary helpers MUST provide deterministic diagnostic code counts across candidate diagnostics.
- **FR-014**: Restore summary helpers MUST fail fast for null result inputs.
- **FR-015**: Restore summary helpers MUST treat null candidate and diagnostic collections as empty collections.
- **FR-016**: Restore summary helpers MUST be usable over manually constructed result objects without services or providers.
- **FR-017**: The system MUST preserve existing lifecycle restore preview and application behavior.
- **FR-018**: The system MUST NOT introduce storage changes, provider changes, service registration, background jobs, audit persistence, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, query planners, policy evaluation, or mutation behavior.
- **FR-019**: Roadmap housekeeping MUST mark `025-operational-hardening` as landed and identify `026-lifecycle-restore-summaries` as the active bounded slice.

### Key Entities

- **Lifecycle Restore Application Summary**: Aggregate counts and status booleans for one restore application result.
- **Lifecycle Restore Preview Summary**: Aggregate counts and status booleans for one restore preview result.
- **Restore Diagnostic Code Count**: Deterministic count for one stable diagnostic code observed in candidate diagnostics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can summarize a restore application result containing at least four mixed candidate statuses and receive correct counts.
- **SC-002**: A host can summarize a restore preview result containing at least four mixed candidate statuses and receive correct counts.
- **SC-003**: Restore summaries produce deterministic distinct resource counts and diagnostic code counts for repeated resources and repeated diagnostics.
- **SC-004**: Summary helpers can be called on manually constructed result objects without service registration or provider setup.
- **SC-005**: Existing lifecycle restore, policy summary, and full test suites continue to pass unchanged.
