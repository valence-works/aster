# Feature Specification: Version History Summaries

**Feature Branch**: `024-version-history-summaries`
**Created**: 2026-05-30
**Status**: Draft
**Input**: Continue with the next bounded Phase 5 slice after batch version history inspection.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize One Resource History (Priority: P1)

As a host developer rendering resource maintenance screens, I want a deterministic summary of one resource history so that I can display counts and simple status indicators without recomputing them in every host.

**Why this priority**: Single-resource history is the existing baseline and the summary should be useful without requiring batch history.

**Independent Test**: Build a resource history with latest, active, draft, protected, possible-candidate, and lifecycle-marked versions, then verify the summary counts.

**Acceptance Scenarios**:

1. **Given** a history with multiple version states, **When** the history is summarized, **Then** the summary reports total versions, latest versions, draft versions, active versions, protected versions, and possible maintenance candidates.
2. **Given** a missing resource history with no versions, **When** the history is summarized, **Then** the summary reports zero counts and preserves the requested resource identity.
3. **Given** a history with lifecycle marker states, **When** the history is summarized, **Then** lifecycle state counts are deterministic and ordered.

---

### User Story 2 - Summarize Batch History Results (Priority: P2)

As a host developer rendering selected-resource dashboards, I want a summary of a batch history result so that I can show aggregate counts across the selected resource set.

**Why this priority**: Batch history landed in the prior slice and needs lightweight host-facing aggregation for dashboards.

**Independent Test**: Build a batch result with populated histories, duplicate lifecycle states, and missing resources, then verify aggregate resource/version counts.

**Acceptance Scenarios**:

1. **Given** a batch result containing populated and empty histories, **When** the batch is summarized, **Then** the summary reports selected resource count, resources with versions, missing resource count, and total version count.
2. **Given** a batch result with protected and possible-candidate versions across resources, **When** the batch is summarized, **Then** protected and candidate counts are aggregated across all histories.
3. **Given** an empty batch result, **When** it is summarized, **Then** all counts are zero and the effective tenant is preserved.

---

### User Story 3 - Keep Summaries Pure and Predictable (Priority: P3)

As a host developer, I want summaries to be pure transformations over result objects so that they are safe to use in tests, UI adapters, and reports without service resolution or provider state.

**Why this priority**: The feature should not change runtime behavior or add infrastructure.

**Independent Test**: Construct result objects manually and summarize them without any registered services or storage provider.

**Acceptance Scenarios**:

1. **Given** manually constructed history result objects, **When** summary helpers are called, **Then** summaries are produced without services, stores, or providers.
2. **Given** null result input, **When** summary helpers are called, **Then** they fail fast with argument validation.
3. **Given** null version or history collections on result objects, **When** summary helpers are called, **Then** they are treated as empty collections.

### Edge Cases

- Null result inputs MUST fail fast with argument validation.
- Null `Versions` or `Histories` collections MUST be treated as empty collections.
- Lifecycle state counts MUST be ordered deterministically.
- Batch missing-resource count MUST be based on histories with zero versions.
- Summaries MUST NOT evaluate policy eligibility or claim definitive prune safety beyond existing maintenance disposition.
- Summaries MUST NOT introduce storage, providers, services, background work, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, query planning, or mutation behavior.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add pure extension helpers and records over existing result models only.
- **Explicitness**: Callers explicitly summarize existing result objects; no hidden reads or discovery.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, provider, debugging, or observability changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a pure summary for a single resource version history result.
- **FR-002**: A single-history summary MUST preserve tenant scope and resource identifier from the source history.
- **FR-003**: A single-history summary MUST report total version count, latest version count, draft version count, active version count, protected version count, and possible maintenance candidate count.
- **FR-004**: A single-history summary MUST report deterministic lifecycle state counts.
- **FR-005**: The system MUST provide a pure summary for a batch resource version history result.
- **FR-006**: A batch summary MUST preserve the effective tenant from the source batch result.
- **FR-007**: A batch summary MUST report selected resource count, resources with versions, missing resource count, total version count, active version count, protected version count, and possible maintenance candidate count.
- **FR-008**: A batch summary MUST report deterministic lifecycle state counts aggregated across all version summaries.
- **FR-009**: Summary helpers MUST fail fast for null result inputs.
- **FR-010**: Summary helpers MUST treat null result collections as empty collections.
- **FR-011**: Summary helpers MUST be usable over manually constructed result objects without services or providers.
- **FR-012**: The system MUST NOT introduce storage changes, provider changes, services, background jobs, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, query planners, policy evaluation, or mutation behavior.

### Key Entities

- **Version History Summary**: Aggregate counts for one resource history.
- **Batch Version History Summary**: Aggregate counts for a selected resource history batch.
- **Lifecycle State Count**: Deterministic count for one lifecycle marker state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can summarize a single history result with at least five mixed version states and receive correct counts.
- **SC-002**: A host can summarize a batch result containing at least three resource histories and receive correct aggregate resource and version counts.
- **SC-003**: Summary helpers can be called on manually constructed result objects without service registration.
- **SC-004**: Empty and null collection edge cases produce deterministic zero-count summaries.
- **SC-005**: Existing version history and policy summary tests continue to pass unchanged.
