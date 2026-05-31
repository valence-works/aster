# Tasks: Schema Upgrade Summaries

**Input**: Design documents from `/specs/029-schema-upgrade-summaries/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required for each user story because this is public SDK behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature has the expected bounded SDK shape before implementation.

- [ ] T001 Confirm active feature context points at `specs/029-schema-upgrade-summaries` in `.specify/feature.json` and `AGENTS.md`
- [ ] T002 Confirm no new dependencies, storage changes, provider changes, service registrations, schedulers, audit persistence, public SQL, or public `IQueryable<Resource>` are planned in `specs/029-schema-upgrade-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared summary surface used by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 Create summary model shell and extension class in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`
- [ ] T004 [P] Create focused test file shell in `test/Aster.Tests/SchemaVersions/ResourceSchemaUpgradeSummaryTests.cs`

**Checkpoint**: Summary file and test file exist; user story implementation can begin.

---

## Phase 3: User Story 1 - Summarize Schema Status Results (Priority: P1) MVP

**Goal**: Hosts can summarize schema status inspection results with deterministic status, upgrade-needed, blocking, and unknown-lineage counts.

**Independent Test**: Create mixed `ResourceSchemaStatusResult` objects, summarize them, and assert total/status/upgrade/blocking/unknown counts.

### Tests for User Story 1

- [ ] T005 [US1] Add mixed schema status summary tests in `test/Aster.Tests/SchemaVersions/ResourceSchemaUpgradeSummaryTests.cs`
- [ ] T006 [US1] Add empty/null schema status collection tests in `test/Aster.Tests/SchemaVersions/ResourceSchemaUpgradeSummaryTests.cs`

### Implementation for User Story 1

- [ ] T007 [US1] Implement `ResourceSchemaStatusCount` and `ResourceSchemaStatusSummary` in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`
- [ ] T008 [US1] Implement `ToSummary(this IEnumerable<ResourceSchemaStatusResult>? results)` with deterministic status counts in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`
- [ ] T009 [US1] Run `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceSchemaUpgradeSummaryTests"` and confirm US1 tests pass

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Summarize Schema Upgrade Results (Priority: P2)

**Goal**: Hosts can summarize schema upgrade outcomes with deterministic status, upgraded-resource, carried-forward aspect, and definition-version counts.

**Independent Test**: Create upgraded/no-op `ResourceSchemaUpgradeResult` objects, summarize them, and assert processed/status/resource/aspect/version counts.

### Tests for User Story 2

- [ ] T010 [US2] Add mixed schema upgrade summary tests in `test/Aster.Tests/SchemaVersions/ResourceSchemaUpgradeSummaryTests.cs`
- [ ] T011 [US2] Add unknown source version and blank carried-forward aspect key tests in `test/Aster.Tests/SchemaVersions/ResourceSchemaUpgradeSummaryTests.cs`
- [ ] T012 [US2] Add empty/null schema upgrade collection tests in `test/Aster.Tests/SchemaVersions/ResourceSchemaUpgradeSummaryTests.cs`

### Implementation for User Story 2

- [ ] T013 [US2] Implement `ResourceSchemaUpgradeStatusCount`, `ResourceSchemaDefinitionVersionCount`, and `ResourceSchemaCarriedForwardAspectKeyCount` in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`
- [ ] T014 [US2] Implement `ResourceSchemaUpgradeSummary` in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`
- [ ] T015 [US2] Implement `ToSummary(this IEnumerable<ResourceSchemaUpgradeResult>? results)` with deterministic status, version, and aspect-key counts in `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs`
- [ ] T016 [US2] Run `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceSchemaUpgradeSummaryTests"` and confirm US1/US2 tests pass

**Checkpoint**: User Stories 1 and 2 are independently functional and testable.

---

## Phase 5: User Story 3 - Preserve Pure, Bounded Behavior (Priority: P3)

**Goal**: Summary helpers remain pure object transformations and do not change existing schema upgrade behavior.

**Independent Test**: Run existing schema version tests and full solution tests after introducing summaries.

### Tests for User Story 3

- [ ] T017 [US3] Verify existing schema version behavior with `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceSchemaVersionServiceTests"`
- [ ] T018 [US3] Verify full test suite with `dotnet test Aster.sln`

### Implementation for User Story 3

- [ ] T019 [US3] Review `src/core/Aster.Core/Models/Instances/ResourceSchemaUpgradeSummaries.cs` for no store/provider/service/scheduler/audit/query/mutation behavior
- [ ] T020 [US3] Run `dotnet build Aster.sln /m:1`

**Checkpoint**: Existing behavior remains unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final validation.

- [ ] T021 Update `specs/029-schema-upgrade-summaries/quickstart.md` if implementation names differ from the planned contract
- [ ] T022 Update `AGENTS.md` recent changes entry from planning context to landed implementation wording
- [ ] T023 Re-run Constitution Check and remove any unnecessary abstraction, dependency, provider, storage, scheduler, audit, query, or mutation scope
- [ ] T024 Run `git diff --check`
- [ ] T025 Mark all tasks complete in `specs/029-schema-upgrade-summaries/tasks.md`

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
- US2 test tasks T010, T011, and T012 can be drafted together before T013-T015.
- Documentation and final checks in T021-T024 can be reviewed independently after implementation.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement US1 status summaries and run focused tests.
3. Stop and validate status summary behavior independently.

### Incremental Delivery

1. Add US1 status summaries.
2. Add US2 upgrade summaries.
3. Run US3 compatibility validation and final build.
4. Complete docs/task cleanup.
