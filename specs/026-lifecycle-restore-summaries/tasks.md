# Tasks: Lifecycle Restore Summaries

**Input**: Design documents from `/specs/026-lifecycle-restore-summaries/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required by the feature specification because summary behavior is pure and independently testable.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the active slice and no dependency or provider setup is needed.

- [ ] T001 Confirm `.specify/feature.json` points to `specs/026-lifecycle-restore-summaries`
- [ ] T002 Confirm `AGENTS.md` points to `specs/026-lifecycle-restore-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared summary model file used by both preview and application stories.

- [ ] T003 [P] Add restore summary record contracts in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestoreSummaries.cs`
- [ ] T004 Add shared deterministic diagnostic-count helper in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestoreSummaries.cs`

**Checkpoint**: Summary models and shared helpers are available for user stories.

---

## Phase 3: User Story 1 - Summarize Restore Application Results (Priority: P1) MVP

**Goal**: Summarize write-side restore application results with deterministic counts.

**Independent Test**: Manually construct an application result with mixed statuses and diagnostics and verify all summary fields.

### Tests for User Story 1

- [ ] T005 [P] [US1] Add mixed-status application summary test in `test/Aster.Tests/Lifecycle/ResourceLifecycleRestoreSummaryTests.cs`
- [ ] T006 [P] [US1] Add application null/empty collection tests in `test/Aster.Tests/Lifecycle/ResourceLifecycleRestoreSummaryTests.cs`

### Implementation for User Story 1

- [ ] T007 [US1] Implement `ToSummary(this ResourceLifecycleRestoreApplicationResult result)` in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestoreSummaries.cs`

**Checkpoint**: User Story 1 is independently testable with focused summary tests.

---

## Phase 4: User Story 2 - Summarize Restore Preview Results (Priority: P2)

**Goal**: Summarize non-mutating restore preview results with deterministic counts.

**Independent Test**: Manually construct a preview result with mixed statuses and diagnostics and verify all summary fields.

### Tests for User Story 2

- [ ] T008 [P] [US2] Add mixed-status preview summary test in `test/Aster.Tests/Lifecycle/ResourceLifecycleRestoreSummaryTests.cs`
- [ ] T009 [P] [US2] Add preview null/empty collection tests in `test/Aster.Tests/Lifecycle/ResourceLifecycleRestoreSummaryTests.cs`

### Implementation for User Story 2

- [ ] T010 [US2] Implement `ToSummary(this ResourceLifecycleRestorePreviewResult result)` in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestoreSummaries.cs`

**Checkpoint**: User Story 2 is independently testable with focused summary tests.

---

## Phase 5: User Story 3 - Keep Restore Summaries Pure and Predictable (Priority: P3)

**Goal**: Verify summaries do not require services, providers, storage, or restore execution.

**Independent Test**: Construct result objects directly and summarize them without building a service provider.

### Tests for User Story 3

- [ ] T011 [P] [US3] Add manual-construction purity coverage in `test/Aster.Tests/Lifecycle/ResourceLifecycleRestoreSummaryTests.cs`
- [ ] T012 [P] [US3] Add diagnostic ordering and blank-code filtering coverage in `test/Aster.Tests/Lifecycle/ResourceLifecycleRestoreSummaryTests.cs`

### Implementation for User Story 3

- [ ] T013 [US3] Ensure restore summary helpers perform no service/provider/storage access in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestoreSummaries.cs`

**Checkpoint**: All user stories are independently covered.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, roadmap, and validation.

- [ ] T014 [P] Update `docs/ExecutionRoadmap.md` to mark 025 landed and make 026 active
- [ ] T015 [P] Update `AGENTS.md` active technology and recent-change context for 026
- [ ] T016 Run focused tests: `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceLifecycleRestoreSummaryTests"`
- [ ] T017 Run full tests: `dotnet test Aster.sln`
- [ ] T018 Run build: `dotnet build Aster.sln /m:1`
- [ ] T019 Run whitespace validation: `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational.
- **User Story 2 (Phase 4)**: Depends on Foundational.
- **User Story 3 (Phase 5)**: Depends on User Story 1 and User Story 2 helper shape.
- **Polish (Phase 6)**: Depends on all selected user stories.

### User Story Dependencies

- **User Story 1 (P1)**: MVP and independently testable after foundational records exist.
- **User Story 2 (P2)**: Independently testable after foundational records exist.
- **User Story 3 (P3)**: Verifies purity and deterministic helper behavior across both summaries.

### Parallel Opportunities

- T003 can be prepared independently from T004 but lands in the same file.
- T005/T006 and T008/T009 are conceptually parallel but edit the same test file, so they should be applied carefully.
- T014 and T015 can be updated independently once implementation is complete.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement User Story 1 application summary and focused tests.
3. Run focused tests.

### Incremental Delivery

1. Add User Story 2 preview summary.
2. Add User Story 3 purity and deterministic edge-case coverage.
3. Run full validation and update roadmap/agent context.

## Notes

- Keep implementation in records/extensions only.
- Do not add services, interfaces, provider registrations, storage, or dependencies.
- Mark each task complete as it is implemented.
