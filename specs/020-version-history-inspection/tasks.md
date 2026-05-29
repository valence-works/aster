# Tasks: Resource Version History Inspection

**Input**: Design documents from `/specs/020-version-history-inspection/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include focused tests because this is SDK behavior with tenant/provider compatibility requirements.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm current project shape and reusable patterns before editing.

- [X] T001 Review existing resource version, activation, lifecycle marker, and DI patterns in `src/core/Aster.Core/`
- [X] T002 [P] Review existing tenant and SQLite provider parity test patterns in `test/Aster.Tests/Tenancy/` and `test/Aster.Tests/SqliteJson/`
- [X] T003 [P] Review docs style in `src/core/Aster.Core/README.md` and `src/persistence/Aster.Persistence.SqliteJson/README.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared contracts/models required by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add `IResourceVersionHistoryService` and `IResourceActivationStateReader` contracts in `src/core/Aster.Core/Abstractions/IResourceVersionHistoryService.cs`
- [X] T005 Add `ResourceVersionHistoryRequest`, `ResourceVersionHistoryResult`, `ResourceVersionSummary`, and `ResourceVersionMaintenanceDisposition` models in `src/core/Aster.Core/Models/Instances/ResourceVersionHistory.cs`
- [X] T006 Update in-memory resource store to implement activation-state reads in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs`
- [X] T007 Register the history service and activation-state reader in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Inspect One Resource History (Priority: P1) MVP

**Goal**: Hosts can read ordered version summaries for one resource and see latest, draft, and active-channel state.

**Independent Test**: Create several versions, activate selected versions, request history, and verify ordered summaries with latest/draft/active-channel flags.

### Tests for User Story 1

- [X] T008 [P] [US1] Add ordered latest/draft/active-channel history tests covering at least five versions in `test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs`
- [X] T009 [P] [US1] Add missing-resource and invalid-resource-id tests in `test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs`

### Implementation for User Story 1

- [X] T010 [US1] Implement `ResourceVersionHistoryService` orchestration over version reads and activation reads in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`
- [X] T011 [US1] Ensure history summaries sort versions and active channels deterministically in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`
- [X] T012 [US1] Ensure missing resources return empty results and invalid request shape follows existing validation patterns in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Include Lifecycle and Maintenance Signals (Priority: P2)

**Goal**: History summaries include current lifecycle marker state and conservative maintenance hints.

**Independent Test**: Apply archive or soft-delete markers, request history, and verify lifecycle state plus protected/possible-candidate disposition.

### Tests for User Story 2

- [X] T013 [P] [US2] Add lifecycle marker state tests in `test/Aster.Tests/Versioning/ResourceVersionHistoryLifecycleTests.cs`
- [X] T014 [P] [US2] Add maintenance disposition tests for latest, active, and historical inactive versions in `test/Aster.Tests/Versioning/ResourceVersionHistoryLifecycleTests.cs`

### Implementation for User Story 2

- [X] T015 [US2] Integrate lifecycle marker reads into `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`
- [X] T016 [US2] Implement protected and possible-candidate maintenance disposition mapping in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Preserve Tenant Boundaries and Provider Compatibility (Priority: P3)

**Goal**: History inspection stays tenant-scoped and returns equivalent semantics for in-memory and SQLite JSON providers.

**Independent Test**: Create matching resource identifiers in two tenants and equivalent SQLite state, then verify only requested-tenant state appears with matching summary semantics.

### Tests for User Story 3

- [X] T017 [P] [US3] Add tenant isolation history tests in `test/Aster.Tests/Tenancy/TenantResourceVersionHistoryTests.cs`
- [X] T018 [P] [US3] Add SQLite JSON version history parity tests in `test/Aster.Tests/SqliteJson/SqliteJsonResourceVersionHistoryTests.cs`

### Implementation for User Story 3

- [X] T019 [US3] Implement SQLite JSON activation-state reader support in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T020 [US3] Register SQLite JSON activation-state reader support in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [X] T021 [US3] Verify tenant-scoped version, activation, and lifecycle reads are composed without cross-tenant leakage in `src/core/Aster.Core/Services/ResourceVersionHistoryService.cs`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, roadmap, cleanup, and verification.

- [X] T022 [P] Update core SDK documentation for version history inspection in `src/core/Aster.Core/README.md`
- [X] T023 [P] Update SQLite JSON provider documentation for history support in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T024 [P] Update execution roadmap current position and landed/next-slice notes in `docs/ExecutionRoadmap.md`
- [X] T025 Update `AGENTS.md` recent changes for `020-version-history-inspection`
- [X] T026 Run `dotnet test Aster.sln`
- [X] T027 Run `dotnet build Aster.sln /m:1`
- [X] T028 Run `git diff --check`
- [X] T029 Re-run constitution check against implemented design and remove any unnecessary abstractions or duplication before final review

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **US1 (Phase 3)**: Depends on Foundation.
- **US2 (Phase 4)**: Depends on Foundation and can be tested after service composition exists.
- **US3 (Phase 5)**: Depends on Foundation and can be validated independently with tenant/provider fixtures.
- **Polish (Phase 6)**: Depends on desired user stories being complete.

### User Story Dependencies

- **US1**: MVP and should be implemented first.
- **US2**: Adds lifecycle and maintenance signals over the US1 result shape.
- **US3**: Adds tenant/provider coverage and SQLite activation-state reader implementation.

### Parallel Opportunities

- T002 and T003 can run in parallel during setup.
- T008 and T009 can be drafted in parallel.
- T013 and T014 can be drafted in parallel.
- T017 and T018 can be drafted in parallel.
- T022, T023, and T024 can be updated in parallel after implementation stabilizes.

## Parallel Example: User Story 1

```text
Task: "T008 [P] [US1] Add ordered latest/draft/active-channel history tests in test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs"
Task: "T009 [P] [US1] Add missing-resource and invalid-resource-id tests in test/Aster.Tests/Versioning/ResourceVersionHistoryServiceTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete setup and foundational contracts/models.
2. Add failing US1 tests.
3. Implement `ResourceVersionHistoryService`.
4. Validate US1 independently.

### Incremental Delivery

1. US1 delivers a usable version timeline.
2. US2 adds lifecycle and maintenance signals.
3. US3 adds tenant/provider compatibility.
4. Polish updates docs and validates the full solution.

## Notes

- Keep the service read-only.
- Keep maintenance hints conservative; do not call policy evaluation.
- Do not introduce storage migrations, public SQL, public `IQueryable<Resource>`, provider registries, or background jobs.
