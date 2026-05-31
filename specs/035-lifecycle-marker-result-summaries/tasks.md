# Tasks: Lifecycle Marker Result Summaries

**Input**: Design documents from `/specs/035-lifecycle-marker-result-summaries/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/lifecycle-marker-result-summaries.md, quickstart.md

**Tests**: Required. This slice is a public SDK reporting affordance and must be covered by focused unit tests before implementation.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup

**Purpose**: Confirm the working branch and feature artifacts are ready.

- [ ] T001 Confirm branch `035-lifecycle-marker-result-summaries` is active and working tree contains only intended 035 changes
- [ ] T002 Confirm Constitution Check gates in `/Users/sipke/Projects/ValenceWorks/aster/specs/035-lifecycle-marker-result-summaries/plan.md` still pass before implementation

---

## Phase 2: Foundational

**Purpose**: Identify existing marker result and policy diagnostic summary patterns before writing code.

- [ ] T003 Review marker result model in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarker.cs`
- [ ] T004 Review policy diagnostic summary patterns in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Policies/ResourcePolicyValidationSummaries.cs`
- [ ] T005 Review lifecycle marker service compatibility tests in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerServiceTests.cs` and `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerConflictTests.cs`
- [ ] T006 Confirm no new dependencies, service registration, provider changes, storage changes, public SQL, public `IQueryable<Resource>`, or mutation behavior are needed

**Checkpoint**: Existing patterns are understood and implementation can proceed.

---

## Phase 3: User Story 1 - Summarize Marker Write Results (Priority: P1) MVP

**Goal**: Hosts can summarize one `ResourceLifecycleMarkerResult` through one public helper.

**Independent Test**: Construct successful and failed marker results, call `ToSummary()`, and verify success, marker presence, marker state counts, affected resource counts, and diagnostic counts.

### Tests for User Story 1

- [ ] T007 [US1] Add failing successful single-result summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerResultSummaryTests.cs`
- [ ] T008 [US1] Add failing failed single-result summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerResultSummaryTests.cs`
- [ ] T009 [US1] Add null single-result assertion in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerResultSummaryTests.cs`

### Implementation for User Story 1

- [ ] T010 [US1] Add marker summary count records to `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs`
- [ ] T011 [US1] Add `ResourceLifecycleMarkerResultSummary` to `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs`
- [ ] T012 [US1] Add `ToSummary(this ResourceLifecycleMarkerResult result)` to `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs`
- [ ] T013 [US1] Run `dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleMarkerResultSummaryTests"`

**Checkpoint**: P1 summary behavior works independently.

---

## Phase 4: User Story 2 - Aggregate Multiple Marker Results (Priority: P2)

**Goal**: Hosts can summarize manually collected marker result sets without a batch marker service.

**Independent Test**: Construct mixed marker results and verify deterministic aggregate counts and null-handling behavior.

### Tests for User Story 2

- [ ] T014 [US2] Add failing mixed enumerable summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerResultSummaryTests.cs`
- [ ] T015 [US2] Add failing null and empty enumerable summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerResultSummaryTests.cs`
- [ ] T016 [US2] Add failing null nested diagnostics and blank keyed field summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Policies/LifecycleMarkerResultSummaryTests.cs`

### Implementation for User Story 2

- [ ] T017 [US2] Add `ToSummary(this IEnumerable<ResourceLifecycleMarkerResult>? results)` to `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs`
- [ ] T018 [US2] Add deterministic marker state, marker resource, diagnostic path, and diagnostic resource count helpers in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs`
- [ ] T019 [US2] Reuse existing `ResourcePolicyDiagnosticCodeCounter` for diagnostic code counts in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs`
- [ ] T020 [US2] Run `dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleMarkerResultSummaryTests"`

**Checkpoint**: Batch-style host summaries are deterministic and null-safe.

---

## Phase 5: User Story 3 - Preserve Marker Service Behavior (Priority: P3)

**Goal**: The new helper remains a pure reporting affordance and existing marker service behavior stays unchanged.

**Independent Test**: Run existing marker service tests and full solution validation.

### Tests for User Story 3

- [ ] T021 [US3] Run `dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleMarkerServiceTests|FullyQualifiedName~LifecycleMarkerConflictTests"`
- [ ] T022 [US3] Run `dotnet test Aster.sln`
- [ ] T023 [US3] Run `dotnet build Aster.sln /m:1`

### Implementation for User Story 3

- [ ] T024 [US3] Confirm `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Instances/ResourceLifecycleMarkerSummaries.cs` has no service resolution, provider access, storage access, marker writes, policy evaluation, lifecycle dispatch, or mutation behavior
- [ ] T025 [US3] Confirm no DI registration, provider, storage, public SQL, public `IQueryable<Resource>`, or query planner files changed

**Checkpoint**: Compatibility and non-goals are verified.

---

## Phase 6: Documentation & Polish

**Purpose**: Keep feature artifacts and repository context aligned with the shipped implementation.

- [ ] T026 [P] Update `/Users/sipke/Projects/ValenceWorks/aster/specs/035-lifecycle-marker-result-summaries/quickstart.md` if implementation names differ from plan
- [ ] T027 [P] Update `/Users/sipke/Projects/ValenceWorks/aster/AGENTS.md` recent change entry from planning context to shipped implementation
- [ ] T028 [P] Update `/Users/sipke/Projects/ValenceWorks/aster/docs/ExecutionRoadmap.md` if final scope changes during implementation
- [ ] T029 Re-run Constitution Check and verify no unnecessary abstractions, dependencies, service registrations, provider changes, or mutation paths were introduced
- [ ] T030 Mark all completed tasks in `/Users/sipke/Projects/ValenceWorks/aster/specs/035-lifecycle-marker-result-summaries/tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1.
- **Phase 3 US1**: Depends on Phase 2 and delivers MVP.
- **Phase 4 US2**: Depends on Phase 3 implementation surface.
- **Phase 5 US3**: Depends on US1 and US2.
- **Phase 6 Polish**: Depends on all desired user stories.

### User Story Dependencies

- **US1**: Independent MVP after foundational review.
- **US2**: Depends on the US1 summary helper shape.
- **US3**: Depends on completed helper behavior so compatibility can be verified.

### Parallel Opportunities

- T003, T004, and T005 can be reviewed in parallel.
- T007, T008, and T009 can be written together.
- T014, T015, and T016 can be written together after US1 surface exists.
- T026, T027, and T028 can run in parallel during polish if no implementation names changed.

## Implementation Strategy

1. Add failing tests for US1 and US2 in a focused marker result summary test class.
2. Implement the summary records and helpers in a new instance model summary file.
3. Verify focused tests, compatibility tests, full tests, and build.
4. Update docs/context and mark tasks complete.
