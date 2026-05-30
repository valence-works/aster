# Tasks: Version History Summaries

**Input**: Design documents from `/specs/024-version-history-summaries/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include focused tests because this is public SDK behavior and mirrors policy summary helpers.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm current summary and version history patterns before editing.

- [X] T001 Review policy summary records/extensions in `src/core/Aster.Core/Models/Policies/ResourcePolicyApplicationSummaries.cs`
- [X] T002 [P] Review policy summary tests in `test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs`
- [X] T003 [P] Review version history result models in `src/core/Aster.Core/Models/Instances/ResourceVersionHistory.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared summary model and helper surface.

- [X] T004 Add version history summary records and extension class in `src/core/Aster.Core/Models/Instances/ResourceVersionHistorySummaries.cs`
- [X] T005 Implement deterministic lifecycle-state count helper in `src/core/Aster.Core/Models/Instances/ResourceVersionHistorySummaries.cs`

---

## Phase 3: User Story 1 - Summarize One Resource History (Priority: P1) MVP

**Goal**: Hosts can summarize one resource history with deterministic version-state counts.

**Independent Test**: Construct one history with mixed version states and verify all counts.

### Tests for User Story 1

- [X] T006 [P] [US1] Add single-history mixed count tests in `test/Aster.Tests/Versioning/ResourceVersionHistorySummaryTests.cs`
- [X] T007 [P] [US1] Add empty single-history tests in `test/Aster.Tests/Versioning/ResourceVersionHistorySummaryTests.cs`

### Implementation for User Story 1

- [X] T008 [US1] Implement `ToSummary(this ResourceVersionHistoryResult)` in `src/core/Aster.Core/Models/Instances/ResourceVersionHistorySummaries.cs`

---

## Phase 4: User Story 2 - Summarize Batch History Results (Priority: P2)

**Goal**: Hosts can summarize selected-resource batch history results.

**Independent Test**: Construct a batch result with populated and missing histories and verify aggregate counts.

### Tests for User Story 2

- [X] T009 [P] [US2] Add batch aggregate count tests in `test/Aster.Tests/Versioning/ResourceVersionHistorySummaryTests.cs`
- [X] T010 [P] [US2] Add empty batch summary tests in `test/Aster.Tests/Versioning/ResourceVersionHistorySummaryTests.cs`

### Implementation for User Story 2

- [X] T011 [US2] Implement `ToSummary(this ResourceVersionHistoryBatchResult)` in `src/core/Aster.Core/Models/Instances/ResourceVersionHistorySummaries.cs`

---

## Phase 5: User Story 3 - Keep Summaries Pure and Predictable (Priority: P3)

**Goal**: Summary helpers work over manually constructed results without services and handle invalid/null shapes predictably.

**Independent Test**: Call summary helpers over manually constructed result objects with null collections and null inputs.

### Tests for User Story 3

- [X] T012 [P] [US3] Add null input and null collection tests in `test/Aster.Tests/Versioning/ResourceVersionHistorySummaryTests.cs`
- [X] T013 [P] [US3] Add no-service manual result test in `test/Aster.Tests/Versioning/ResourceVersionHistorySummaryTests.cs`

### Implementation for User Story 3

- [X] T014 [US3] Ensure summary helpers fail fast for null result inputs and treat null collections as empty in `src/core/Aster.Core/Models/Instances/ResourceVersionHistorySummaries.cs`

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, roadmap, cleanup, and validation.

- [X] T015 [P] Update core SDK documentation for version history summaries in `src/core/Aster.Core/README.md`
- [X] T016 [P] Update execution roadmap to mark 023 landed and 024 in progress in `docs/ExecutionRoadmap.md`
- [X] T017 Update `AGENTS.md` recent changes for `024-version-history-summaries`
- [X] T018 Run `dotnet test Aster.sln`
- [X] T019 Run `dotnet build Aster.sln /m:1`
- [X] T020 Run `git diff --check`
- [X] T021 Re-run constitution check against implemented design and remove unnecessary abstractions or duplication before final review

## Dependencies & Execution Order

- Setup precedes all implementation.
- Foundational records/extensions precede user stories.
- US1 is the MVP and should land before batch aggregation.
- US2 builds on shared helper logic.
- US3 verifies purity and null-shape behavior.
- Polish follows implementation.

## Parallel Opportunities

- T002 and T003 can run in parallel.
- T006 and T007 can be drafted in parallel.
- T009 and T010 can be drafted in parallel.
- T012 and T013 can be drafted in parallel.
- T015 and T016 can be updated in parallel.

## Implementation Strategy

1. Add the pure model/extension file.
2. Add tests for single-history summaries.
3. Add tests for batch summaries.
4. Add null/manual result tests.
5. Update docs and roadmap.
6. Run full validation.

## Notes

- Keep summaries pure; do not add service registration.
- Do not add storage, provider, policy, query, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, or mutation behavior.
