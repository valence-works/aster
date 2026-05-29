# Tasks: Policy Application Summaries

**Input**: Design documents from `/specs/022-policy-application-summaries/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by the feature specification for application summaries, pruning summaries, deterministic diagnostic counts, and bounded pure helper behavior.

**Organization**: Tasks are grouped by user story so the marker-based application summary can ship as the MVP before pruning summary parity.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature context and current policy result shapes.

- [X] T001 Verify feature documents and checklist are present in specs/022-policy-application-summaries/
- [X] T002 Inspect existing policy application result models in src/core/Aster.Core/Models/Policies/ResourcePolicyApplication.cs and src/core/Aster.Core/Models/Policies/ResourcePolicyPruningApplication.cs
- [X] T003 Inspect current policy result tests in test/Aster.Tests/Policies/PolicyApplicationResultTests.cs and test/Aster.Tests/Policies/PolicyPruningApplicationResultTests.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared summary model surface used by both stories without adding services or dependencies.

- [X] T004 Add shared diagnostic code count and summary helpers in src/core/Aster.Core/Models/Policies/ResourcePolicyApplicationSummaries.cs
- [X] T005 Add documentation for policy summaries in src/core/Aster.Core/README.md

**Checkpoint**: Shared model surface exists and docs state summaries are pure reporting helpers.

---

## Phase 3: User Story 1 - Summarize Policy Application Results (Priority: P1) MVP

**Goal**: Hosts can summarize marker-based policy application results.

**Independent Test**: Build a result containing applied, already satisfied, skipped, and failed candidates; request a summary; verify counts, booleans, affected resource count, and diagnostic code counts.

### Tests for User Story 1

- [X] T006 [P] [US1] Add mixed marker-based application summary tests in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs
- [X] T007 [P] [US1] Add empty marker-based application summary tests in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs

### Implementation for User Story 1

- [X] T008 [US1] Implement ResourcePolicyApplicationResult summary generation in src/core/Aster.Core/Models/Policies/ResourcePolicyApplicationSummaries.cs
- [X] T009 [US1] Run focused policy application summary tests in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs

**Checkpoint**: Marker-based application summaries are independently usable.

---

## Phase 4: User Story 2 - Summarize Policy Pruning Results (Priority: P2)

**Goal**: Hosts can summarize policy pruning application results with the same reporting semantics.

**Independent Test**: Build a pruning result containing pruned, already pruned, skipped, and failed candidates; request a summary; verify counts, booleans, affected target count, and diagnostic code counts.

### Tests for User Story 2

- [X] T010 [P] [US2] Add mixed pruning summary tests in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs
- [X] T011 [P] [US2] Add empty pruning summary tests in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs

### Implementation for User Story 2

- [X] T012 [US2] Implement ResourcePolicyPruningApplicationResult summary generation in src/core/Aster.Core/Models/Policies/ResourcePolicyApplicationSummaries.cs
- [X] T013 [US2] Run focused pruning summary tests in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs

**Checkpoint**: Both result families have deterministic summary helpers.

---

## Phase 5: User Story 3 - Preserve Bounded SDK Semantics (Priority: P3)

**Goal**: Summary generation remains a pure transformation and does not imply audit persistence or execution.

**Independent Test**: Verify summaries can be generated from manually constructed result objects with no service provider or storage setup.

### Tests for User Story 3

- [X] T014 [P] [US3] Add tests proving summary generation ignores blank diagnostic codes in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs
- [X] T015 [P] [US3] Add tests proving summaries are generated from manually constructed result objects without service dependencies in test/Aster.Tests/Policies/PolicyApplicationSummaryTests.cs

### Implementation for User Story 3

- [X] T016 [US3] Review summary implementation to ensure it has no service, storage, query, provider, lifecycle, scheduler, authorization, or background dependencies in src/core/Aster.Core/Models/Policies/ResourcePolicyApplicationSummaries.cs

**Checkpoint**: Bounded pure-helper semantics are covered.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Keep roadmap/context current and validate the whole solution.

- [X] T017 [P] Update docs/ExecutionRoadmap.md to mark 021 landed and 022 in progress
- [X] T018 [P] Ensure AGENTS.md includes 022 technology and recent-change context
- [X] T019 Validate quickstart behavior against specs/022-policy-application-summaries/quickstart.md
- [X] T020 Run dotnet test Aster.sln
- [X] T021 Run dotnet build Aster.sln /m:1
- [X] T022 Run git diff --check
- [X] T023 Review implementation against specs/022-policy-application-summaries/spec.md and mark all completed tasks in specs/022-policy-application-summaries/tasks.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup and blocks user story work.
- **User Story 1 (Phase 3)**: Depends on foundational summary surface.
- **User Story 2 (Phase 4)**: Depends on shared summary surface and can reuse US1 helper patterns.
- **User Story 3 (Phase 5)**: Depends on both summary implementations.
- **Polish (Phase 6)**: Depends on all stories.

### User Story Dependencies

- **User Story 1 (P1)**: MVP.
- **User Story 2 (P2)**: Adds pruning parity.
- **User Story 3 (P3)**: Confirms bounded implementation semantics.

### Parallel Opportunities

- T006 and T007 can be drafted together.
- T010 and T011 can be drafted together.
- T014 and T015 cover separate edge-case assertions.
- T017 and T018 touch separate documentation/context files.

---

## Implementation Strategy

### MVP First

1. Add shared summary records and helpers.
2. Implement marker-based policy application summary.
3. Validate marker-based summary tests.

### Incremental Delivery

1. Add pruning summary parity.
2. Add bounded-semantics edge coverage.
3. Update docs/context and run full validation.
