# Feature Specification: Policy Preview Summaries

**Feature Branch**: `027-policy-preview-summaries`
**Created**: 2026-05-31
**Status**: Draft
**Input**: Continue with the next bounded Phase 5 slice after lifecycle restore summaries.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Policy Preview Candidates (Priority: P1)

As a host developer rendering policy preview screens, I want a deterministic summary of policy preview candidates so that operators can see how many resources and versions are affected before selecting application actions.

**Why this priority**: Policy preview is the first operator decision point. Hosts already have application summaries; preview summaries complete the pre-write reporting story without adding execution behavior.

**Independent Test**: Construct a policy preview with archive, soft-delete, prune-preview, and retain candidates, then verify total candidate, distinct resource, distinct resource-version target, outcome, and policy-kind counts.

**Acceptance Scenarios**:

1. **Given** a policy preview with mixed outcomes, **When** it is summarized, **Then** outcome counts are deterministic.
2. **Given** a policy preview with mixed policy kinds, **When** it is summarized, **Then** policy-kind counts are deterministic.
3. **Given** candidates repeat resource identifiers and resource-version targets, **When** the preview is summarized, **Then** distinct resource and version-target counts use ordinal identity.

---

### User Story 2 - Summarize Preview Diagnostics (Priority: P2)

As a host developer troubleshooting policy previews, I want stable diagnostic counts in the preview summary so that UI and logs can show why preview evaluation is incomplete or partially blocked.

**Why this priority**: Policy validation and preview diagnostics already exist. A deterministic diagnostic rollup avoids duplicated host code.

**Independent Test**: Construct a preview with repeated diagnostics, blank diagnostic codes, and no candidates, then verify diagnostic code counts, diagnostic booleans, and empty-preview behavior.

**Acceptance Scenarios**:

1. **Given** preview diagnostics contain repeated codes, **When** the preview is summarized, **Then** diagnostic code counts are grouped and ordered deterministically.
2. **Given** diagnostics contain blank codes, **When** the preview is summarized, **Then** blank diagnostic codes are ignored.
3. **Given** a preview has diagnostics but no candidates, **When** the preview is summarized, **Then** diagnostic counts are still reported.

---

### User Story 3 - Keep Policy Preview Summaries Pure (Priority: P3)

As a host developer, I want policy preview summaries to be pure transformations over existing result objects so that they are safe in tests, UI adapters, logs, and reports without service resolution or provider state.

**Why this priority**: The feature should not change policy evaluation or application semantics. It should remain a deletable convenience over already-returned data.

**Independent Test**: Construct preview result objects manually and summarize them without any registered services or storage provider.

**Acceptance Scenarios**:

1. **Given** a manually constructed preview result, **When** summary helpers are called, **Then** summaries are produced without services, stores, providers, or mutations.
2. **Given** null preview result input, **When** summary helpers are called, **Then** they fail fast with argument validation.
3. **Given** null candidate and diagnostic collections on the preview result, **When** summary helpers are called, **Then** they are treated as empty collections.

### Edge Cases

- Null preview result input MUST fail fast with argument validation.
- Null candidate and diagnostic collections MUST be treated as empty collections.
- Blank resource identifiers MUST be ignored for distinct resource counts.
- Resource-version target counts MUST include only candidates with nonblank resource identifiers and resource versions.
- Blank diagnostic codes MUST be ignored for diagnostic code counts.
- Outcome and policy-kind counts MUST be ordered deterministically by enum value.
- Diagnostic code counts MUST be ordered deterministically by ordinal code.
- Summaries MUST NOT perform reads, writes, validation, policy evaluation, lifecycle marker changes, storage access, provider access, background work, or query planning.

### Constitution Alignment *(mandatory)*

- **Simplicity**: Add pure extension helpers and records over existing policy preview result models only. No service, registry, provider, workflow, or audit store is introduced.
- **Explicitness**: Callers explicitly summarize existing result objects. There is no hidden discovery, scanning, ambient state, or automatic reporting behavior.
- **Dependencies**: None.
- **Operational Impact**: No deployment, storage, migration, provider setup, debugging, observability, or runtime behavior changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a pure summary for policy evaluation preview results.
- **FR-002**: A policy preview summary MUST preserve the effective tenant and optional evaluation timestamp from the source preview.
- **FR-003**: A policy preview summary MUST report total candidate count.
- **FR-004**: A policy preview summary MUST report the number of distinct nonblank resource identifiers represented by preview candidates.
- **FR-005**: A policy preview summary MUST report the number of distinct resource-version targets represented by preview candidates.
- **FR-006**: A policy preview summary MUST report deterministic counts by policy outcome.
- **FR-007**: A policy preview summary MUST report deterministic counts by policy kind.
- **FR-008**: A policy preview summary MUST report deterministic diagnostic code counts across preview diagnostics.
- **FR-009**: A policy preview summary MUST expose whether any preview diagnostics are present.
- **FR-010**: A policy preview summary MUST expose whether the preview has no diagnostics.
- **FR-011**: Policy preview summary helpers MUST fail fast for null result inputs.
- **FR-012**: Policy preview summary helpers MUST treat null candidate and diagnostic collections as empty collections.
- **FR-013**: Policy preview summary helpers MUST be usable over manually constructed preview result objects without services or providers.
- **FR-014**: The system MUST preserve existing policy validation, preview, application, pruning, restore, query, portability, and lifecycle hook behavior.
- **FR-015**: The system MUST NOT introduce storage changes, provider changes, service registration, background jobs, audit persistence, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, query planners, policy evaluation changes, or mutation behavior.
- **FR-016**: Roadmap housekeeping MUST mark `026-lifecycle-restore-summaries` as landed and identify `027-policy-preview-summaries` as the active bounded slice.

### Key Entities

- **Policy Preview Summary**: Aggregate counts and status booleans for one policy evaluation preview.
- **Policy Outcome Count**: Deterministic count for one previewed policy outcome.
- **Policy Kind Count**: Deterministic count for one previewed policy kind.
- **Policy Diagnostic Code Count**: Existing deterministic count for one stable diagnostic code observed in preview diagnostics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host can summarize a policy preview containing at least four mixed policy outcomes and receive correct outcome counts.
- **SC-002**: A host can summarize a policy preview containing at least three policy kinds and receive correct kind counts.
- **SC-003**: Policy preview summaries produce deterministic distinct resource, distinct version-target, and diagnostic code counts for repeated values.
- **SC-004**: Summary helpers can be called on manually constructed preview objects without service registration or provider setup.
- **SC-005**: Existing policy preview, policy summary, lifecycle restore summary, and full test suites continue to pass unchanged.
