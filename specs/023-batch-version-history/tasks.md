# Tasks: Batch Version History Inspection

**Input**: Design documents from `/specs/023-batch-version-history/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include focused tests because this is SDK behavior with tenant/provider compatibility requirements.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm current history service and test patterns before editing.

- [X] T001 Review existing single-resource history service and models in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs` and `src/core/Aster.Core/Models/Instances/ResourceVersionHistory.cs`
- [X] T002 [P] Review existing versioning, tenant, and SQLite history tests in `test/Aster.Tests/Versioning/`, `test/Aster.Tests/Tenancy/`, and `test/Aster.Tests/SqliteJson/`
- [X] T003 [P] Review docs and roadmap update style in `src/core/Aster.Core/README.md`, `src/persistence/Aster.Persistence.SqliteJson/README.md`, and `docs/ExecutionRoadmap.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared contracts/models required by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add `GetHistoriesAsync` to `IResourceVersionHistoryService` in `src/core/Aster.Core/Abstractions/IResourceVersionHistoryService.cs`
- [X] T005 Add `ResourceVersionHistoryBatchRequest` and `ResourceVersionHistoryBatchResult` models in `src/core/Aster.Core/Models/Instances/ResourceVersionHistory.cs`
- [X] T006 Refactor reusable per-resource history projection helpers in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Inspect Selected Histories Together (Priority: P1) MVP

**Goal**: Hosts can read histories for a selected distinct resource set in one service call.

**Independent Test**: Create multiple resources with versions and activations, request their histories together, and verify ordered per-resource histories match single-resource semantics.

### Tests for User Story 1

- [X] T007 [P] [US1] Add batch multi-resource and duplicate-order tests in `test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs`
- [X] T008 [P] [US1] Add batch-vs-single semantic parity test in `test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs`

### Implementation for User Story 1

- [X] T009 [US1] Implement batch resource ID normalization and `GetHistoriesAsync` orchestration in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`
- [X] T010 [US1] Ensure batch version summaries preserve existing version ordering, active-channel ordering, latest, draft, lifecycle, and maintenance semantics in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Preserve Tenant Boundaries (Priority: P2)

**Goal**: Batch history reads stay within one effective tenant and preserve default-tenant compatibility.

**Independent Test**: Store matching identifiers in two tenants, request one tenant, and verify only that tenant's histories are returned.

### Tests for User Story 2

- [X] T011 [P] [US2] Add batch tenant isolation and default-tenant tests in `test/Aster.Tests/Tenancy/TenantResourceVersionHistoryTests.cs`

### Implementation for User Story 2

- [X] T012 [US2] Verify batch service reads versions, activation states, and lifecycle markers with the resolved effective tenant in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Handle Empty and Missing Selections Predictably (Priority: P3)

**Goal**: Empty, invalid, and missing selections behave predictably across in-memory and SQLite providers.

**Independent Test**: Request empty selections, blank identifiers, and missing resources and verify deterministic validation/results.

### Tests for User Story 3

- [X] T013 [P] [US3] Add empty selection, blank identifier, null request, null ID collection, and missing-resource tests in `test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs`
- [X] T014 [P] [US3] Add SQLite JSON batch history parity tests in `test/Aster.Tests/SqliteJson/SqliteJsonResourceVersionHistoryTests.cs`

### Implementation for User Story 3

- [X] T015 [US3] Ensure empty selections return empty batch results and missing resources return empty per-resource histories in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`
- [X] T016 [US3] Ensure invalid request shape uses existing fail-fast argument validation patterns in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, roadmap, cleanup, and verification.

- [X] T017 [P] Update core SDK documentation for batch version history inspection in `src/core/Aster.Core/README.md`
- [X] T018 [P] Update SQLite JSON provider documentation for batch history compatibility in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T019 [P] Update execution roadmap to mark 022 landed and 023 in progress in `docs/ExecutionRoadmap.md`
- [X] T020 Update `AGENTS.md` recent changes for `023-batch-version-history`
- [X] T021 Run `dotnet test Aster.sln`
- [X] T022 Run `dotnet build Aster.sln /m:1`
- [X] T023 Run `git diff --check`
- [X] T024 Re-run constitution check against implemented design and remove unnecessary abstractions or duplication before final review

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **US1 (Phase 3)**: Depends on Foundation.
- **US2 (Phase 4)**: Depends on Foundation and can be tested after batch service composition exists.
- **US3 (Phase 5)**: Depends on Foundation and can be validated independently with edge-case and provider fixtures.
- **Polish (Phase 6)**: Depends on desired user stories being complete.

### User Story Dependencies

- **US1**: MVP and should be implemented first.
- **US2**: Adds tenant isolation coverage over the US1 result shape.
- **US3**: Adds edge-case behavior and SQLite parity coverage.

### Parallel Opportunities

- T002 and T003 can run in parallel during setup.
- T007 and T008 can be drafted in parallel.
- T013 and T014 can be drafted in parallel.
- T017, T018, and T019 can be updated in parallel after implementation stabilizes.

## Parallel Example: User Story 1

```text
Task: "T007 [P] [US1] Add batch multi-resource and duplicate-order tests in test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs"
Task: "T008 [P] [US1] Add batch-vs-single semantic parity test in test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete setup and foundational contracts/models.
2. Add failing US1 tests.
3. Implement `GetHistoriesAsync` by reusing existing history composition.
4. Validate US1 independently.

### Incremental Delivery

1. US1 delivers selected-resource batch timelines.
2. US2 confirms tenant boundaries.
3. US3 confirms edge-case and SQLite compatibility.
4. Polish updates docs and validates the full solution.

## Notes

- Keep the service read-only.
- Keep selection explicit and bounded to caller-supplied resource IDs.
- Do not introduce storage migrations, public SQL, public `IQueryable<Resource>`, provider registries, runtime scanning, automatic discovery, query planners, or background jobs.
