# Tasks: Query Validation Summaries

**Input**: Design documents from `/specs/030-query-validation-summaries/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required for each user story because this is public SDK behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature has the expected bounded SDK shape before implementation.

- [x] T001 Confirm active feature context points at `specs/030-query-validation-summaries` in `.specify/feature.json` and `AGENTS.md`
- [x] T002 Confirm no new dependencies, storage changes, provider changes, service registrations, query planner, public SQL, public `IQueryable<Resource>`, execution behavior changes, or mutation behavior are planned in `specs/030-query-validation-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared summary surface used by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Create summary model shell and extension class in `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs`
- [x] T004 [P] Create focused test file shell in `test/Aster.Tests/Querying/QueryValidationSummaryTests.cs`

**Checkpoint**: Summary file and test file exist; user story implementation can begin.

---

## Phase 3: User Story 1 - Summarize Validation Failure Codes (Priority: P1) MVP

**Goal**: Hosts can summarize validation results with validity booleans, total failure counts, and deterministic failure-code counts.

**Independent Test**: Create valid and invalid `QueryValidationResult` objects, summarize them, and assert validity/total/code counts.

### Tests for User Story 1

- [x] T005 [US1] Add success validation summary tests in `test/Aster.Tests/Querying/QueryValidationSummaryTests.cs`
- [x] T006 [US1] Add mixed failure-code summary tests in `test/Aster.Tests/Querying/QueryValidationSummaryTests.cs`

### Implementation for User Story 1

- [x] T007 [US1] Implement `QueryValidationFailureCodeCount` and `QueryValidationSummary` in `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs`
- [x] T008 [US1] Implement `ToSummary(this QueryValidationResult result)` with total failure and deterministic code counts in `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs`
- [x] T009 [US1] Run `dotnet test Aster.sln --filter "FullyQualifiedName~QueryValidationSummaryTests"` and confirm US1 tests pass

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Summarize Failure Locations and Features (Priority: P2)

**Goal**: Hosts can summarize validation failures by deterministic nonblank path and feature buckets.

**Independent Test**: Create failures with repeated paths/features and blank optional values, summarize them, and assert path/feature counts.

### Tests for User Story 2

- [x] T010 [US2] Add failure path count tests in `test/Aster.Tests/Querying/QueryValidationSummaryTests.cs`
- [x] T011 [US2] Add failure feature count and blank-key tests in `test/Aster.Tests/Querying/QueryValidationSummaryTests.cs`

### Implementation for User Story 2

- [x] T012 [US2] Implement `QueryValidationFailurePathCount` and `QueryValidationFailureFeatureCount` in `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs`
- [x] T013 [US2] Extend `ToSummary(this QueryValidationResult result)` with deterministic path and feature counts in `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs`
- [x] T014 [US2] Run `dotnet test Aster.sln --filter "FullyQualifiedName~QueryValidationSummaryTests"` and confirm US1/US2 tests pass

**Checkpoint**: User Stories 1 and 2 are independently functional and testable.

---

## Phase 5: User Story 3 - Preserve Pure Validation Behavior (Priority: P3)

**Goal**: Summary helpers remain pure object transformations and do not change query validation or execution behavior.

**Independent Test**: Run existing query validator tests and full solution tests after introducing summaries.

### Tests for User Story 3

- [x] T015 [US3] Verify existing query validator behavior with `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceQueryValidatorTests"`
- [x] T016 [US3] Verify full test suite with `dotnet test Aster.sln`

### Implementation for User Story 3

- [x] T017 [US3] Review `src/core/Aster.Core/Models/Querying/QueryValidationSummaries.cs` for no store/provider/service/query-planner/execution/public-SQL/public-IQueryable/mutation behavior
- [x] T018 [US3] Run `dotnet build Aster.sln /m:1`

**Checkpoint**: Existing behavior remains unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final validation.

- [x] T019 Update `specs/030-query-validation-summaries/quickstart.md` if implementation names differ from the planned contract
- [x] T020 Update `AGENTS.md` recent changes entry from planning context to landed implementation wording
- [x] T021 Re-run Constitution Check and remove any unnecessary abstraction, dependency, provider, storage, service registration, query planner, execution, public SQL, public `IQueryable<Resource>`, or mutation scope
- [x] T022 Run `git diff --check`
- [x] T023 Mark all tasks complete in `specs/030-query-validation-summaries/tasks.md`

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
- US1 test tasks T005 and T006 can be drafted together before T007/T008.
- US2 test tasks T010 and T011 can be drafted together before T012/T013.
- Documentation and final checks in T019-T022 can be reviewed independently after implementation.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement US1 code/count summaries and run focused tests.
3. Stop and validate code-count summary behavior independently.

### Incremental Delivery

1. Add US1 failure-code summaries.
2. Add US2 path and feature summaries.
3. Run US3 compatibility validation and final build.
4. Complete docs/task cleanup.
