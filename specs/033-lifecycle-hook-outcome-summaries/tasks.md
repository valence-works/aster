# Tasks: Lifecycle Hook Outcome Summaries

**Input**: Design documents from `/specs/033-lifecycle-hook-outcome-summaries/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required because this is public SDK behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task serves (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature has the expected bounded SDK shape before implementation.

- [x] T001 Confirm active feature context points at `specs/033-lifecycle-hook-outcome-summaries` in `.specify/feature.json` and `AGENTS.md`
- [x] T002 Confirm no new dependencies, storage changes, provider changes, service registrations, schedulers, audit persistence, lifecycle dispatcher behavior changes, hook execution behavior changes, public SQL, public `IQueryable<Resource>`, query planner, or mutation behavior are planned in `specs/033-lifecycle-hook-outcome-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared summary surface used by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Create summary model shell and extension class in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcomeSummaries.cs`
- [x] T004 [P] Create focused test file shell in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`

**Checkpoint**: Summary file and test file exist; user story implementation can begin.

---

## Phase 3: User Story 1 - Summarize Hook Outcome Health (Priority: P1) MVP

**Goal**: Hosts can summarize lifecycle hook outcome health with total outcome counts, status counts, and success/failure booleans.

**Independent Test**: Create continue, rejected, and failed `LifecycleHookOutcome` objects, summarize them, and assert total outcome, status, and boolean counts.

### Tests for User Story 1

- [x] T005 [US1] Add successful single-outcome summary tests in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`
- [x] T006 [US1] Add mixed outcome status summary tests in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`
- [x] T007 [US1] Add null single-outcome and null/empty enumerable tests in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`

### Implementation for User Story 1

- [x] T008 [US1] Implement `LifecycleHookOutcomeSummary` and status/code count records in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcomeSummaries.cs`
- [x] T009 [US1] Implement single-outcome and enumerable `ToSummary()` helpers with status and outcome-code counts in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcomeSummaries.cs`
- [x] T010 [US1] Run `dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleHookOutcomeSummaryTests"` and confirm US1 tests pass

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Group Hook Diagnostics Deterministically (Priority: P2)

**Goal**: Hosts can summarize nested lifecycle hook diagnostics by diagnostic code, lifecycle point, and hook type.

**Independent Test**: Create mixed outcomes with nested diagnostics and verify deterministic counts for each grouping dimension.

### Tests for User Story 2

- [x] T011 [US2] Add diagnostic code and lifecycle point grouping tests in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`
- [x] T012 [US2] Add hook type grouping and blank key filtering tests in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`
- [x] T013 [US2] Add null nested diagnostic collection tests in `test/Aster.Tests/Lifecycle/LifecycleHookOutcomeSummaryTests.cs`

### Implementation for User Story 2

- [x] T014 [US2] Implement diagnostic code, lifecycle point, and hook type count records in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcomeSummaries.cs`
- [x] T015 [US2] Extend `ToSummary()` helpers with deterministic diagnostic total and grouped diagnostic counts in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcomeSummaries.cs`
- [x] T016 [US2] Run `dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleHookOutcomeSummaryTests"` and confirm US1/US2 tests pass

**Checkpoint**: User Stories 1 and 2 are independently functional and testable.

---

## Phase 5: User Story 3 - Preserve Hook Execution Behavior (Priority: P3)

**Goal**: Summary helpers remain pure object transformations and do not change lifecycle dispatcher or hook execution behavior.

**Independent Test**: Run existing lifecycle hook dispatcher tests and full solution tests after introducing summaries.

### Tests for User Story 3

- [x] T017 [US3] Verify existing dispatcher behavior with `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceLifecycleHookDispatcherTests"`
- [x] T018 [US3] Verify full test suite with `dotnet test Aster.sln`

### Implementation for User Story 3

- [x] T019 [US3] Review `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcomeSummaries.cs` for no storage/provider/service/scheduler/audit/dispatcher/hook-execution/public-SQL/public-IQueryable/query-planner/mutation behavior
- [x] T020 [US3] Run `dotnet build Aster.sln /m:1`

**Checkpoint**: Existing behavior remains unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final validation.

- [x] T021 Update `specs/033-lifecycle-hook-outcome-summaries/quickstart.md` if implementation names differ from the planned contract
- [x] T022 Update `AGENTS.md` recent changes entry from planning context to landed implementation wording
- [x] T023 Re-run Constitution Check and remove any unnecessary abstraction, dependency, provider, storage, service registration, scheduler, audit persistence, dispatcher behavior, hook execution behavior, public SQL, public `IQueryable<Resource>`, query planner, or mutation scope
- [x] T024 Run `git diff --check`
- [x] T025 Mark all tasks complete in `specs/033-lifecycle-hook-outcome-summaries/tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational
- **User Story 2 (Phase 4)**: Depends on Foundational; can be reasoned independently but uses the same summary file
- **User Story 3 (Phase 5)**: Depends on US1 and US2 implementation
- **Polish (Phase 6)**: Depends on all desired user stories

### User Story Dependencies

- **User Story 1 (P1)**: MVP; no dependency on other user stories after Foundation
- **User Story 2 (P2)**: Independent from US1 semantically, but edits the same files and should run after US1 in this single-agent implementation
- **User Story 3 (P3)**: Validates bounded behavior after US1/US2 are complete

### Parallel Opportunities

- T004 can run in parallel with T003.
- US1 test tasks T005-T007 can be drafted together before T008/T009.
- US2 test tasks T011-T013 can be drafted together before T014/T015.
- Documentation and final checks in T021-T024 can be reviewed independently after implementation.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement US1 lifecycle hook outcome health summaries and run focused tests.
3. Stop and validate health summary behavior independently.

### Incremental Delivery

1. Add US1 outcome health summaries.
2. Add US2 deterministic diagnostic grouping.
3. Run US3 compatibility validation and final build.
4. Complete docs/task cleanup.
