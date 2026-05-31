# Tasks: Portable Validation Summaries

**Input**: Design documents from `/specs/034-portable-validation-summaries/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/portable-validation-summaries.md, quickstart.md

**Tests**: Required. This slice is a small public SDK reporting affordance and must be covered by focused unit tests before implementation.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup

**Purpose**: Confirm the working branch and feature artifacts are ready.

- [x] T001 Confirm branch `034-portable-validation-summaries` is active and working tree contains only intended 034 changes
- [x] T002 Confirm Constitution Check gates in `/Users/sipke/Projects/ValenceWorks/aster/specs/034-portable-validation-summaries/plan.md` still pass before implementation

---

## Phase 2: Foundational

**Purpose**: Identify existing summary patterns and portability validation contracts before writing code.

- [x] T003 Review existing portability summary implementation in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [x] T004 Review existing portability summary tests in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortableResultSummaryTests.cs`
- [x] T005 Review existing portability validation behavior tests in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortabilityValidationTests.cs`
- [x] T006 Confirm no new dependencies, service registration, provider changes, storage changes, public SQL, public `IQueryable<Resource>`, or mutation behavior are needed

**Checkpoint**: Existing patterns are understood and implementation can proceed.

---

## Phase 3: User Story 1 - Summarize Snapshot Validation (Priority: P1) MVP

**Goal**: Hosts can summarize `PortableSnapshotValidationResult` validity and diagnostics through one public helper.

**Independent Test**: Construct valid and invalid validation results with mixed diagnostics, call `ToSummary()`, and verify validity, error state, total diagnostic count, severity counts, and code counts.

### Tests for User Story 1

- [x] T007 [US1] Add failing valid-result summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortableResultSummaryTests.cs`
- [x] T008 [US1] Add failing mixed-diagnostic validation summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortableResultSummaryTests.cs`

### Implementation for User Story 1

- [x] T009 [US1] Add `PortableValidationSummary` to `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [x] T010 [US1] Add `ToSummary(this PortableSnapshotValidationResult result)` to `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [x] T011 [US1] Reuse existing severity/code count helpers for validation summary diagnostics in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [x] T012 [US1] Run `dotnet test Aster.sln --filter "FullyQualifiedName~PortableResultSummaryTests"`

**Checkpoint**: P1 summary behavior works independently.

---

## Phase 4: User Story 2 - Handle Sparse Results Predictably (Priority: P2)

**Goal**: Null diagnostic collections and blank diagnostic codes behave consistently with existing portability summaries.

**Independent Test**: Construct validation results with null diagnostics and blank diagnostic codes, then verify counts remain deterministic and defensive host guard code is unnecessary.

### Tests for User Story 2

- [x] T013 [US2] Add failing null-diagnostics validation summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortableResultSummaryTests.cs`
- [x] T014 [US2] Add failing blank-code validation summary test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortableResultSummaryTests.cs`
- [x] T015 [US2] Add null-root validation summary assertion to existing null input test in `/Users/sipke/Projects/ValenceWorks/aster/test/Aster.Tests/Portability/PortableResultSummaryTests.cs`

### Implementation for User Story 2

- [x] T016 [US2] Ensure validation summary treats null diagnostics as empty in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [x] T017 [US2] Ensure validation summary omits blank diagnostic codes while preserving total and severity counts in `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [x] T018 [US2] Run `dotnet test Aster.sln --filter "FullyQualifiedName~PortableResultSummaryTests"`

**Checkpoint**: Sparse validation results are summarized predictably.

---

## Phase 5: User Story 3 - Preserve Existing Portability Behavior (Priority: P3)

**Goal**: The new helper remains a pure reporting affordance and existing portability behavior stays unchanged.

**Independent Test**: Run existing portability validation tests and full solution validation.

### Tests for User Story 3

- [x] T019 [US3] Run `dotnet test Aster.sln --filter "FullyQualifiedName~PortabilityValidationTests"`
- [x] T020 [US3] Run `dotnet test Aster.sln`
- [x] T021 [US3] Run `dotnet build Aster.sln /m:1`

### Implementation for User Story 3

- [x] T022 [US3] Confirm `/Users/sipke/Projects/ValenceWorks/aster/src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs` has no service resolution, provider access, storage access, import/export execution, validation execution, or mutation behavior
- [x] T023 [US3] Confirm no DI registration, provider, storage, public SQL, public `IQueryable<Resource>`, or query planner files changed

**Checkpoint**: Compatibility and non-goals are verified.

---

## Phase 6: Documentation & Polish

**Purpose**: Keep feature artifacts and repository context aligned with the shipped implementation.

- [x] T024 [P] Update `/Users/sipke/Projects/ValenceWorks/aster/specs/034-portable-validation-summaries/quickstart.md` if implementation names differ from plan
- [x] T025 [P] Update `/Users/sipke/Projects/ValenceWorks/aster/AGENTS.md` recent change entry from planning context to shipped implementation
- [x] T026 [P] Update `/Users/sipke/Projects/ValenceWorks/aster/docs/ExecutionRoadmap.md` if final scope changes during implementation
- [x] T027 Re-run Constitution Check and verify no unnecessary abstractions, dependencies, service registrations, or provider changes were introduced
- [x] T028 Mark all completed tasks in `/Users/sipke/Projects/ValenceWorks/aster/specs/034-portable-validation-summaries/tasks.md`

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
- T007 and T008 can be written together.
- T013, T014, and T015 can be written together after US1 surface exists.
- T024, T025, and T026 can run in parallel during polish if no implementation names changed.

## Implementation Strategy

1. Add failing tests for US1 and US2 in the existing portability summary test class.
2. Implement the summary record and helper by extending existing portability summary code.
3. Verify focused tests, compatibility tests, full tests, and build.
4. Update docs/context and mark tasks complete.
