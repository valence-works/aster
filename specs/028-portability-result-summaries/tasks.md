# Tasks: Portability Result Summaries

**Input**: Design documents from `/specs/028-portability-result-summaries/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required by the feature specification because summary behavior is pure and independently testable.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the active slice and no dependency or provider setup is needed.

- [ ] T001 Confirm `.specify/feature.json` points to `specs/028-portability-result-summaries`
- [ ] T002 Confirm `AGENTS.md` points to `specs/028-portability-result-summaries/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared portability summary records and deterministic count helpers.

- [ ] T003 [P] Add portability summary record contracts in `src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [ ] T004 Add shared diagnostic and mapping count helpers in `src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`

**Checkpoint**: Summary models and shared helpers are available for user stories.

---

## Phase 3: User Story 1 - Summarize Portable Export Results (Priority: P1) MVP

**Goal**: Summarize export results with deterministic snapshot, skipped activation, and diagnostic counts.

**Independent Test**: Manually construct an export result with snapshot content, skipped activation entries, and diagnostics and verify all summary fields.

### Tests for User Story 1

- [ ] T005 [P] [US1] Add export summary tests in `test/Aster.Tests/Portability/PortableResultSummaryTests.cs`

### Implementation for User Story 1

- [ ] T006 [US1] Implement `ToSummary(this PortableSnapshotExportResult result)` in `src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`

**Checkpoint**: User Story 1 is independently testable with focused summary tests.

---

## Phase 4: User Story 2 - Summarize Portable Import Preview Results (Priority: P2)

**Goal**: Summarize import previews with deterministic planned counts, mapping reason counts, and diagnostics.

**Independent Test**: Manually construct an import preview with planned counts, mappings, and diagnostics and verify all summary fields.

### Tests for User Story 2

- [ ] T007 [P] [US2] Add import preview summary tests in `test/Aster.Tests/Portability/PortableResultSummaryTests.cs`

### Implementation for User Story 2

- [ ] T008 [US2] Implement `ToSummary(this PortableImportPreview preview)` in `src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`

**Checkpoint**: User Story 2 is independently testable with focused summary tests.

---

## Phase 5: User Story 3 - Summarize Portable Import Results (Priority: P3)

**Goal**: Summarize import results with deterministic actual counts, mapping reason counts, status booleans, and diagnostics.

**Independent Test**: Manually construct import results for imported, no-op, and failed statuses and verify all summary fields.

### Tests for User Story 3

- [ ] T009 [P] [US3] Add import result summary tests in `test/Aster.Tests/Portability/PortableResultSummaryTests.cs`
- [ ] T010 [P] [US3] Add null-input and null-collection tests in `test/Aster.Tests/Portability/PortableResultSummaryTests.cs`

### Implementation for User Story 3

- [ ] T011 [US3] Implement `ToSummary(this PortableImportResult result)` in `src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`
- [ ] T012 [US3] Ensure portability summary helpers perform no service/provider/storage access in `src/core/Aster.Core/Models/Portability/PortableResultSummaries.cs`

**Checkpoint**: All user stories are independently covered.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, roadmap, and validation.

- [ ] T013 [P] Update `docs/ExecutionRoadmap.md` to mark 027 landed and make 028 active
- [ ] T014 [P] Update `AGENTS.md` active technology and recent-change context for 028
- [ ] T015 Run focused tests: `dotnet test Aster.sln --filter "FullyQualifiedName~PortableResultSummaryTests"`
- [ ] T016 Run full tests: `dotnet test Aster.sln`
- [ ] T017 Run build: `dotnet build Aster.sln /m:1`
- [ ] T018 Run whitespace validation: `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational.
- **User Story 2 (Phase 4)**: Depends on Foundational.
- **User Story 3 (Phase 5)**: Depends on Foundational.
- **Polish (Phase 6)**: Depends on all selected user stories.

### Parallel Opportunities

- T003 and T004 can be developed together in the same file with careful coordination.
- T005, T007, T009, and T010 are conceptually parallel but edit the same test file, so they should be applied carefully.
- T013 and T014 can be updated independently once implementation is complete.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Implement User Story 1 export summary and focused tests.
3. Run focused tests.

### Incremental Delivery

1. Add User Story 2 import preview summary.
2. Add User Story 3 import result summary and edge-case coverage.
3. Run full validation and update roadmap/agent context.

## Notes

- Keep implementation in records/extensions only.
- Do not add services, interfaces, provider registrations, storage, recipes, or dependencies.
- Mark each task complete as it is implemented.
