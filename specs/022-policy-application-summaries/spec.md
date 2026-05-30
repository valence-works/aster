# Feature Specification: Policy Application Summaries

**Feature Branch**: `022-policy-application-summaries`
**Created**: 2026-05-29
**Status**: Draft
**Input**: User description: "Add host-facing policy application summary models and helpers over existing policy application and pruning application results. Summaries should provide deterministic status counts, success/failure booleans, diagnostic code counts, and affected resource/version counts for UI/reporting without re-running policy logic or storing audit records. Keep the slice SDK-only and bounded: no storage changes, no scheduler, no authorization engine, no provider registry, no public SQL, no public IQueryable<Resource>, no background jobs, and no broad reporting framework."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Policy Application Results (Priority: P1)

As a host author, I need a deterministic summary of policy application results, so that an operations UI can show how many selected policy candidates applied, were already satisfied, skipped, or failed without each host reimplementing result aggregation.

**Why this priority**: Policy application already returns per-candidate results. Hosts need a stable aggregate surface to present those results consistently after applying archive or soft-delete policy outcomes.

**Independent Test**: Build a policy application result containing applied, already satisfied, skipped, and failed candidate results; request a summary; verify total counts, success/failure booleans, affected resource counts, and diagnostic code counts.

**Acceptance Scenarios**:

1. **Given** a policy application result has only applied and already satisfied candidates, **When** a summary is requested, **Then** the summary reports no failures and marks the operation successful.
2. **Given** a policy application result has failed or skipped candidates, **When** a summary is requested, **Then** the summary reports unsuccessful completion and includes deterministic counts by status.
3. **Given** failed candidates include diagnostics, **When** a summary is requested, **Then** diagnostic codes are counted deterministically.

---

### User Story 2 - Summarize Policy Pruning Results (Priority: P2)

As a host author, I need the same summary shape for policy pruning application results, so that destructive pruning operations can be reported consistently with marker-based policy applications.

**Why this priority**: Pruning application has a different result type and status vocabulary, but hosts still need aggregate counts and diagnostics for operator review.

**Independent Test**: Build a pruning application result containing pruned, already pruned, skipped, and failed candidate results; request a summary; verify status counts, failure state, affected version counts, and diagnostic code counts.

**Acceptance Scenarios**:

1. **Given** a pruning result has pruned and already-pruned candidates only, **When** a summary is requested, **Then** the summary reports no failures and includes affected version counts.
2. **Given** a pruning result has skipped duplicate candidates, **When** a summary is requested, **Then** skipped candidates are counted separately from failures.
3. **Given** a pruning result has failed candidates with diagnostics, **When** a summary is requested, **Then** diagnostic codes are counted deterministically.

---

### User Story 3 - Preserve Bounded SDK Semantics (Priority: P3)

As a host author, I need summaries to be pure views over existing result objects, so that reporting cannot trigger policy re-evaluation, writes, background work, or provider-specific behavior.

**Why this priority**: The feature is a reporting affordance. It must not change policy execution semantics or introduce infrastructure that belongs to a future reporting system.

**Independent Test**: Generate summaries from empty, null-equivalent, and mixed result inputs and verify no store/provider dependencies are required and no policy services are invoked.

**Acceptance Scenarios**:

1. **Given** an empty application result, **When** a summary is requested, **Then** counts are zero and the summary is successful.
2. **Given** an empty pruning result, **When** a summary is requested, **Then** counts are zero and the summary is successful.
3. **Given** summaries are generated, **When** tests inspect dependencies, **Then** no storage, query provider, scheduler, authorization, or lifecycle service is required.

### Edge Cases

- Result contains no candidates.
- Result contains duplicate resource IDs or resource/version pairs.
- Result contains multiple diagnostics with the same code.
- Result contains diagnostics without a code or with blank code text.
- Result contains failed candidates without diagnostics.
- Result contains skipped candidates that should not count as writes.
- Result contains null candidate collections only if an existing result shape can represent that state.

### Constitution Alignment *(mandatory)*

- **Simplicity**: The feature SHOULD add small summary records and pure aggregation helpers over existing result objects. It MUST NOT introduce a reporting service, audit log, event stream, or framework-shaped abstraction.
- **Explicitness**: Hosts explicitly call summary helpers after receiving policy application results. No runtime scanning, implicit execution, or background reporting is introduced.
- **Dependencies**: None.
- **Operational Impact**: No storage schema change, data migration, background process, provider configuration, deployment change, or observability pipeline is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose a host-facing summary for `ResourcePolicyApplicationResult`.
- **FR-002**: Application summaries MUST include total candidate count and counts for applied, already satisfied, skipped, and failed candidates.
- **FR-003**: Application summaries MUST expose a boolean indicating whether the result has failures.
- **FR-004**: Application summaries MUST expose a boolean indicating whether all candidates completed successfully, treating applied and already satisfied candidates as successful and treating skipped or failed candidates as not fully successful.
- **FR-005**: Application summaries MUST include the count of distinct affected resource identifiers represented by candidates that applied or were already satisfied.
- **FR-006**: Application summaries MUST include deterministic diagnostic code counts across candidate diagnostics.
- **FR-007**: System MUST expose a host-facing summary for `ResourcePolicyPruningApplicationResult`.
- **FR-008**: Pruning summaries MUST include total candidate count and counts for pruned, already pruned, skipped, and failed candidates.
- **FR-009**: Pruning summaries MUST expose a boolean indicating whether the result has failures.
- **FR-010**: Pruning summaries MUST expose a boolean indicating whether all candidates completed successfully, treating pruned and already pruned candidates as successful and treating skipped or failed candidates as not fully successful.
- **FR-011**: Pruning summaries MUST include the count of distinct affected resource/version targets represented by candidates that pruned or were already pruned.
- **FR-012**: Summary diagnostic code counts MUST ignore null, empty, or whitespace diagnostic codes.
- **FR-013**: Diagnostic code counts MUST be ordered deterministically by diagnostic code using ordinal comparison.
- **FR-014**: Summary generation MUST be a pure in-memory transformation over existing result objects and MUST NOT call policy evaluation, application services, stores, query providers, lifecycle hooks, schedulers, authorization engines, or background jobs.
- **FR-015**: The feature MUST NOT introduce storage changes, provider registries, runtime scanning, public SQL, public `IQueryable<Resource>`, broad reporting infrastructure, or audit persistence.
- **FR-016**: Documentation MUST explain how hosts can use summaries for UI/reporting and clarify that summaries are not audit records.

### Key Entities *(include if feature involves data)*

- **Policy Application Summary**: Aggregate view over a marker-based policy application result, including status counts, completion booleans, affected resource count, and diagnostic code counts.
- **Policy Pruning Application Summary**: Aggregate view over a pruning application result, including status counts, completion booleans, affected target count, and diagnostic code counts.
- **Diagnostic Code Count**: Deterministic pair of diagnostic code and count for repeated policy diagnostics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can summarize a mixed marker-based policy application result without inspecting individual candidates manually.
- **SC-002**: Hosts can summarize a mixed pruning application result without inspecting individual candidates manually.
- **SC-003**: Diagnostic code counts are stable and ordered deterministically across repeated runs.
- **SC-004**: Existing policy application, pruning, lifecycle, query, portability, SQLite, and versioning tests continue to pass without new storage or provider setup.
