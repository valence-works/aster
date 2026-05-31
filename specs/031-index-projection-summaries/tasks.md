# Tasks: Index Projection Summaries

**Input**: Design documents from `/specs/031-index-projection-summaries/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required for each user story because this is public SDK behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature has the expected bounded SDK shape before implementation.

- [ ] T001 Confirm active feature context points at `specs/031-index-projection-summaries` in `.specify/feature.json` and `AGENTS.md`
- [ ] T002 Confirm no new dependencies, storage changes, provider changes, service registrations, physical indexes, query planner, public SQL, public `IQueryable<Resource>`, execution behavior changes, or mutation behavior are planned in `specs/031-index-projection-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared summary surface used by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 Create summary model shell and extension class in `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs`
- [ ] T004 [P] Create focused test file shell in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`

**Checkpoint**: Summary file and test file exist; user story implementation can begin.

---

## Phase 3: User Story 1 - Summarize Projection Validation Failures (Priority: P1) MVP

**Goal**: Provider authors can summarize projection validation failures with validity booleans and deterministic failure counts by code, field name, and source.

**Independent Test**: Create success and invalid `IndexProjectionValidationResult` objects, summarize them, and assert validity/total/code/field/source counts.

### Tests for User Story 1

- [ ] T005 [US1] Add success validation summary tests in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`
- [ ] T006 [US1] Add mixed validation failure summary tests in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`
- [ ] T007 [US1] Add blank validation failure key tests in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`

### Implementation for User Story 1

- [ ] T008 [US1] Implement projection failure count records and `IndexProjectionValidationSummary` in `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs`
- [ ] T009 [US1] Implement `ToSummary(this IndexProjectionValidationResult result)` with deterministic failure counts in `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs`
- [ ] T010 [US1] Run `dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionSummaryTests"` and confirm US1 tests pass

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Summarize Projection Evaluation Results (Priority: P2)

**Goal**: Hosts can summarize projection evaluation values and failures with deterministic value field-type, value field-name, and failure counts.

**Independent Test**: Create `IndexProjectionEvaluationResult` objects with successful values and failures, summarize them, and assert value/failure totals and deterministic count lists.

### Tests for User Story 2

- [ ] T011 [US2] Add successful evaluation value summary tests in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`
- [ ] T012 [US2] Add mixed evaluation value/failure summary tests in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`
- [ ] T013 [US2] Add empty/null nested evaluation collection tests in `test/Aster.Tests/Querying/IndexProjectionSummaryTests.cs`

### Implementation for User Story 2

- [ ] T014 [US2] Implement projection value count records and `IndexProjectionEvaluationSummary` in `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs`
- [ ] T015 [US2] Implement `ToSummary(this IndexProjectionEvaluationResult result)` with deterministic value and failure counts in `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs`
- [ ] T016 [US2] Run `dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionSummaryTests"` and confirm US1/US2 tests pass

**Checkpoint**: User Stories 1 and 2 are independently functional and testable.

---

## Phase 5: User Story 3 - Preserve Pure Indexing Behavior (Priority: P3)

**Goal**: Summary helpers remain pure object transformations and do not change projection validation/evaluation or query behavior.

**Independent Test**: Run existing projection validation/evaluation tests and full solution tests after introducing summaries.

### Tests for User Story 3

- [ ] T017 [US3] Verify existing projection declaration behavior with `dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionDeclarationTests"`
- [ ] T018 [US3] Verify existing projection evaluation behavior with `dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionEvaluationTests"`
- [ ] T019 [US3] Verify full test suite with `dotnet test Aster.sln`

### Implementation for User Story 3

- [ ] T020 [US3] Review `src/core/Aster.Core/Models/Querying/IndexProjectionSummaries.cs` for no store/provider/service/physical-index/query-planner/execution/public-SQL/public-IQueryable/mutation behavior
- [ ] T021 [US3] Run `dotnet build Aster.sln /m:1`

**Checkpoint**: Existing behavior remains unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final validation.

- [ ] T022 Update `specs/031-index-projection-summaries/quickstart.md` if implementation names differ from the planned contract
- [ ] T023 Update `AGENTS.md` recent changes entry from planning context to landed implementation wording
- [ ] T024 Re-run Constitution Check and remove any unnecessary abstraction, dependency, provider, storage, service registration, physical index, query planner, execution, public SQL, public `IQueryable<Resource>`, or mutation scope
- [ ] T025 Run `git diff --check`
- [ ] T026 Mark all tasks complete in `specs/031-index-projection-summaries/tasks.md`

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
- Documentation and final checks in T022-T025 can be reviewed independently after implementation.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement US1 projection validation summaries and run focused tests.
3. Stop and validate validation summary behavior independently.

### Incremental Delivery

1. Add US1 validation summaries.
2. Add US2 evaluation summaries.
3. Run US3 compatibility validation and final build.
4. Complete docs/task cleanup.
