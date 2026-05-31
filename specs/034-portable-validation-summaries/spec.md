# Feature Specification: Portable Validation Summaries

**Feature Branch**: `034-portable-validation-summaries`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Next slice after lifecycle hook outcome summaries. Continue the bounded host-reporting workstream by filling the remaining portability validation summary gap without changing storage, providers, validation behavior, import/export behavior, public SQL, or public IQueryable<Resource>."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize Snapshot Validation (Priority: P1)

As a host developer, I want to turn a portable snapshot validation result into deterministic counts so I can show validation health without parsing the raw diagnostic list.

**Why this priority**: `PortableSnapshotValidationResult` is the remaining portability result sibling not covered by the current summary helpers, so hosts still need custom diagnostic aggregation for validation.

**Independent Test**: Construct a validation result with mixed diagnostic severities and codes, call the summary helper, and verify validity, error state, total diagnostic count, severity counts, and code counts.

**Acceptance Scenarios**:

1. **Given** a valid portable snapshot validation result with no diagnostics, **When** a host creates a summary, **Then** the summary reports valid, no errors, zero diagnostics, and empty count collections.
2. **Given** an invalid validation result with warning and error diagnostics, **When** a host creates a summary, **Then** the summary reports invalid, has errors, total diagnostic count, deterministic severity counts, and deterministic diagnostic code counts.

---

### User Story 2 - Handle Sparse Results Predictably (Priority: P2)

As a host developer, I want null diagnostic collections and blank diagnostic codes to be handled consistently with existing portability summaries so defensive reporting code is unnecessary.

**Why this priority**: Existing summary helpers treat null nested collections as empty and ignore blank diagnostic codes. Validation summaries should not introduce a special case.

**Independent Test**: Construct a validation result with a null diagnostic collection and another with blank diagnostic codes, then verify the summary returns zero counts for null diagnostics and excludes blank codes from code counts.

**Acceptance Scenarios**:

1. **Given** a validation result whose diagnostics collection is null, **When** a host creates a summary, **Then** the summary treats diagnostics as empty.
2. **Given** diagnostics with blank codes, **When** a host creates a summary, **Then** blank codes are excluded from diagnostic code counts while their severities still contribute to severity counts.

---

### User Story 3 - Preserve Existing Portability Behavior (Priority: P3)

As a library maintainer, I want the new validation summary to reuse the existing portability summary shape without changing validation, import, export, storage, or provider behavior.

**Why this priority**: This is a reporting affordance only. It must remain easy to delete and must not become a new service, registry, or workflow layer.

**Independent Test**: Run the existing portability result summary tests and portability service tests alongside the new validation summary tests.

**Acceptance Scenarios**:

1. **Given** existing export, import preview, and import result summaries, **When** the new validation summary is added, **Then** their public behavior and tests remain unchanged.
2. **Given** the portability validation service, **When** the new summary helper is added, **Then** validation execution behavior remains unchanged and no service registration is added.

### Edge Cases

- Null `PortableSnapshotValidationResult` input MUST throw `ArgumentNullException`, consistent with existing single-result summary helpers.
- Null `Diagnostics` on a validation result MUST be treated as empty.
- Blank or whitespace diagnostic codes MUST NOT appear in diagnostic code counts.
- Severity counts MUST include every diagnostic severity present in the diagnostics, including diagnostics whose code is blank.
- Diagnostic severity counts MUST be ordered deterministically by severity enum value.
- Diagnostic code counts MUST be ordered deterministically using ordinal string ordering.

### Constitution Alignment *(mandatory)*

- **Simplicity**: This feature SHOULD add one summary record and one extension method over an existing result type. It MUST NOT add services, registries, providers, schedulers, or workflow infrastructure.
- **Explicitness**: Hosts explicitly call `ToSummary()` on a validation result. There is no automatic discovery, runtime scanning, implicit reporting pipeline, or hidden side effect.
- **Dependencies**: None. The implementation MUST use existing C#/.NET and project test dependencies only.
- **Operational Impact**: No deployment, storage, provider, configuration, or observability changes. Local validation remains standard `dotnet test` and `dotnet build`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a public summary type for `PortableSnapshotValidationResult`.
- **FR-002**: The summary MUST expose the source validation validity state from `PortableSnapshotValidationResult.IsValid`.
- **FR-003**: The summary MUST expose whether validation diagnostics include at least one error severity.
- **FR-004**: The summary MUST expose the total diagnostic count.
- **FR-005**: The summary MUST expose deterministic diagnostic severity counts using existing portability diagnostic severity count shape where practical.
- **FR-006**: The summary MUST expose deterministic diagnostic code counts using existing portability diagnostic code count shape where practical.
- **FR-007**: The summary helper MUST treat null validation diagnostic collections as empty.
- **FR-008**: The summary helper MUST throw `ArgumentNullException` for null validation result input.
- **FR-009**: The implementation MUST preserve existing export summary, import preview summary, import summary, validation service, import/export service, storage, provider, and registration behavior.
- **FR-010**: The implementation MUST NOT introduce storage changes, provider-specific behavior, service registration, a reporting framework, public raw SQL, public `IQueryable<Resource>`, query planner behavior, or mutation behavior.

### Key Entities *(include if feature involves data)*

- **Portable Snapshot Validation Summary**: Host-facing aggregate view over `PortableSnapshotValidationResult`; includes validity, error presence, total diagnostics, severity counts, and diagnostic code counts.
- **Portable Diagnostic Severity Count**: Existing deterministic count of diagnostics by `PortableDiagnosticSeverity`.
- **Portable Diagnostic Code Count**: Existing deterministic count of non-blank diagnostic codes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Hosts can summarize any `PortableSnapshotValidationResult` through a single public `ToSummary()` call.
- **SC-002**: Mixed diagnostics produce deterministic severity and code counts in test assertions regardless of input order.
- **SC-003**: Null nested diagnostics and blank diagnostic codes are handled without host-side guard code.
- **SC-004**: Existing portability result summary and portability behavior tests continue to pass without changes to service behavior.
