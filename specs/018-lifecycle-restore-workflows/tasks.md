# Tasks: Lifecycle Restore Workflows

**Input**: Design documents from `/specs/018-lifecycle-restore-workflows/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required by the feature specification's independent tests. Write story tests before implementation and confirm they fail for the expected missing behavior before making them pass.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Phase 1: Setup

**Purpose**: Confirm baseline and feature context before shared implementation.

- [X] T001 Read lifecycle restore requirements in `specs/018-lifecycle-restore-workflows/spec.md`
- [X] T002 Read restore planning and contract decisions in `specs/018-lifecycle-restore-workflows/plan.md`
- [X] T003 Inspect existing lifecycle marker service/store contracts in `src/core/Aster.Core/Abstractions/IResourceLifecycleMarkerStore.cs`
- [X] T004 Inspect existing lifecycle marker implementation in `src/core/Aster.Core/Services/ResourceLifecycleMarkerService.cs`
- [X] T005 Run baseline restore/build with `dotnet restore Aster.sln`

---

## Phase 2: Foundational

**Purpose**: Shared contracts, models, diagnostics, and provider clear capability required by all restore stories.

**CRITICAL**: No user story implementation should begin until these are complete.

- [X] T006 [P] Add `IResourceLifecycleRestoreService` contract in `src/core/Aster.Core/Abstractions/IResourceLifecycleRestoreService.cs`
- [X] T007 Add `IResourceLifecycleMarkerClearStore` provider capability in `src/core/Aster.Core/Abstractions/IResourceLifecycleMarkerStore.cs`
- [X] T008 [P] Add lifecycle restore request/result/status models in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestore.cs`
- [X] T009 [P] Add stable restore diagnostic codes in `src/core/Aster.Core/Models/Policies/ResourcePolicyResults.cs`
- [X] T010 Implement `IResourceLifecycleMarkerClearStore.ClearMarkerAsync` for in-memory markers in `src/core/Aster.Core/InMemory/InMemoryResourceLifecycleMarkerStore.cs`
- [X] T011 Implement `IResourceLifecycleMarkerClearStore.ClearMarkerAsync` for SQLite JSON markers in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T012 Register `IResourceLifecycleMarkerClearStore` and `IResourceLifecycleRestoreService` in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T013 Register SQLite JSON `IResourceLifecycleMarkerClearStore` replacement in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [X] T014 Run foundational compile with `dotnet build Aster.sln /m:1`

**Checkpoint**: Contracts and provider clear capability compile; user story implementation can proceed.

---

## Phase 3: User Story 1 - Restore Marked Resources Explicitly (Priority: P1) MVP

**Goal**: Hosts can explicitly restore selected archived or soft-deleted resources by clearing only matching lifecycle markers while resource versions and activation state remain unchanged.

**Independent Test**: Mark resources archived/soft-deleted, restore selected candidates, verify selected markers are cleared, unselected markers remain, versions remain intact, and activation state remains unchanged.

### Tests for User Story 1

- [X] T015 [P] [US1] Add archive and soft-delete restore success tests in `test/Aster.Tests/Lifecycle/LifecycleRestoreServiceTests.cs`
- [X] T016 [US1] Add subset restore and unselected marker preservation tests in `test/Aster.Tests/Lifecycle/LifecycleRestoreServiceTests.cs`
- [X] T017 [P] [US1] Add resource version and activation preservation tests in `test/Aster.Tests/Lifecycle/LifecycleRestoreActivationTests.cs`
- [X] T018 [P] [US1] Add lifecycle-state query after restore regression test in `test/Aster.Tests/Querying/LifecycleStateQueryTests.cs`

### Implementation for User Story 1

- [X] T019 [US1] Implement `ResourceLifecycleRestoreService.RestoreAsync` success path in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T020 [US1] Add candidate-bounded latest-resource and marker reads by distinct candidate resource IDs to restore application in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T021 [US1] Clear matching archive and soft-delete markers through `IResourceLifecycleMarkerClearStore` in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T022 [US1] Preserve versions and activation state by avoiding resource writer and activation dependencies in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T023 [US1] Run focused P1 tests with `dotnet test Aster.sln --filter "LifecycleRestore|LifecycleStateQuery"`

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Preview Restore Outcomes Before Writing (Priority: P2)

**Goal**: Hosts can preview restore candidates and receive non-mutating outcomes for restorable, already-restored, missing, and mismatched resources.

**Independent Test**: Submit mixed preview candidates and verify one stable non-mutating preview outcome per input while marker state remains unchanged.

### Tests for User Story 2

- [X] T024 [P] [US2] Add preview restorable, already-restored, duplicate-skipped, and empty-request tests in `test/Aster.Tests/Lifecycle/LifecycleRestorePreviewTests.cs`
- [X] T025 [US2] Add preview marker-mismatch and missing-target diagnostic tests in `test/Aster.Tests/Lifecycle/LifecycleRestorePreviewTests.cs`
- [X] T026 [US2] Add preview no-mutation regression tests, including invalid and unsupported candidate no-clear assertions, in `test/Aster.Tests/Lifecycle/LifecycleRestorePreviewTests.cs`

### Implementation for User Story 2

- [X] T027 [US2] Implement `ResourceLifecycleRestoreService.PreviewRestoreAsync` in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T028 [US2] Extract shared candidate validation/evaluation helper for preview and application in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T029 [US2] Ensure preview never calls `ClearMarkerAsync` in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T030 [US2] Run focused P2 tests with `dotnet test Aster.sln --filter "LifecycleRestorePreview|LifecycleRestore"`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Report Deterministic Restore Results (Priority: P3)

**Goal**: Restore application returns deterministic per-candidate results and stable diagnostics for duplicates, invalid input, unsupported states, missing targets, already-restored resources, tenant misses, and marker mismatches.

**Independent Test**: Submit a mixed restore request and verify every candidate receives exactly one stable result; valid unrelated candidates still restore.

### Tests for User Story 3

- [X] T031 [P] [US3] Add restore aggregate counts, empty-request, and one-result-per-input tests in `test/Aster.Tests/Lifecycle/LifecycleRestoreResultTests.cs`
- [X] T032 [US3] Add duplicate candidate determinism tests in `test/Aster.Tests/Lifecycle/LifecycleRestoreResultTests.cs`
- [X] T033 [P] [US3] Add invalid candidate and unsupported state diagnostic tests with no-marker-cleared assertions in `test/Aster.Tests/Lifecycle/LifecycleRestoreDiagnosticsTests.cs`
- [X] T034 [US3] Add missing target, marker-mismatch, and stale-preview-before-apply application diagnostic tests in `test/Aster.Tests/Lifecycle/LifecycleRestoreDiagnosticsTests.cs`
- [X] T035 [P] [US3] Add tenant-scoped restore isolation and omitted default tenant scope tests in `test/Aster.Tests/Tenancy/TenantLifecycleRestoreTests.cs`
- [X] T036 [P] [US3] Add SQLite JSON restore compatibility tests in `test/Aster.Tests/SqliteJson/SqliteJsonLifecycleRestoreTests.cs`

### Implementation for User Story 3

- [X] T037 [US3] Implement invalid candidate and unsupported state diagnostics in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T038 [US3] Implement missing target and tenant-scoped target-not-found diagnostics in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T039 [US3] Implement marker-state mismatch diagnostics that leave current markers untouched in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T040 [US3] Implement duplicate candidate memoization and deterministic skipped/already-restored behavior in `src/core/Aster.Core/Services/ResourceLifecycleRestoreService.cs`
- [X] T041 [US3] Compute preview and application aggregate counts from candidate results in `src/core/Aster.Core/Models/Instances/ResourceLifecycleRestore.cs`
- [X] T042 [US3] Ensure SQLite JSON lifecycle marker deletes are tenant-scoped in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T043 [US3] Run focused P3 tests with `dotnet test Aster.sln --filter "LifecycleRestore|TenantLifecycleRestore|SqliteJsonLifecycleRestore"`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, compatibility, and final verification.

- [X] T044 [P] Update lifecycle marker documentation in `src/core/Aster.Core/README.md`
- [X] T045 [P] Update SQLite JSON provider documentation in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T046 [P] Update roadmap/status documentation in `docs/ExecutionRoadmap.md`
- [X] T047 Validate quickstart examples against implemented APIs in `specs/018-lifecycle-restore-workflows/quickstart.md`
- [X] T048 Run all tests with `dotnet test Aster.sln`
- [X] T049 Run full build with `dotnet build Aster.sln /m:1`
- [X] T050 Run whitespace validation with `git diff --check`
- [X] T051 Re-run constitution check and remove unnecessary abstractions or dependencies in `specs/018-lifecycle-restore-workflows/plan.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; can be implemented after or alongside US1 if shared restore helpers stay compatible.
- **User Story 3 (Phase 5)**: Depends on Foundational and should follow US1/US2 because it hardens shared result and diagnostic behavior.
- **Polish (Phase 6)**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 Restore Marked Resources**: No dependency on other user stories after Foundational.
- **US2 Preview Restore Outcomes**: No product dependency on US1, but implementation should reuse shared validation/evaluation helpers from US1 where available.
- **US3 Deterministic Results**: Builds on shared result models and restore service behavior from US1/US2.

### Parallel Opportunities

- T006, T008, and T009 can be done in parallel after setup.
- T010 and T011 can be done in parallel after T007.
- US1 tests T015, T017, and T018 can be written in parallel.
- US2 test file T024-T026 should be edited serially because all tasks share one file.
- US3 tests T031, T033, T035, and T036 can be written in parallel.
- Documentation tasks T044-T046 can be done in parallel after implementation behavior is stable.

---

## Parallel Example: User Story 1

```text
Task: "T015 [P] [US1] Add archive and soft-delete restore success tests in test/Aster.Tests/Lifecycle/LifecycleRestoreServiceTests.cs"
Task: "T017 [P] [US1] Add resource version and activation preservation tests in test/Aster.Tests/Lifecycle/LifecycleRestoreActivationTests.cs"
Task: "T018 [P] [US1] Add lifecycle-state query after restore regression test in test/Aster.Tests/Querying/LifecycleStateQueryTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T033 [P] [US3] Add invalid candidate and unsupported state diagnostic tests in test/Aster.Tests/Lifecycle/LifecycleRestoreDiagnosticsTests.cs"
Task: "T035 [P] [US3] Add tenant-scoped restore isolation tests in test/Aster.Tests/Tenancy/TenantLifecycleRestoreTests.cs"
Task: "T036 [P] [US3] Add SQLite JSON restore compatibility tests in test/Aster.Tests/SqliteJson/SqliteJsonLifecycleRestoreTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundational contracts, models, clear capability, and DI registration.
3. Write failing US1 tests.
4. Implement restore application success path.
5. Validate with focused US1 tests before moving to preview or diagnostic hardening.

### Incremental Delivery

1. Add US1 restore application for explicit archive/soft-delete marker clearing.
2. Add US2 non-mutating preview over the same candidate evaluation rules.
3. Add US3 deterministic diagnostics, duplicates, tenant isolation, and SQLite compatibility.
4. Complete docs and full-suite validation.

### Guardrails

- Do not add a scheduler, policy engine, authorization system, provider registry, public SQL, public `IQueryable<Resource>`, destructive pruning writes, or a lifecycle state machine.
- Do not rewrite resource versions or change activation state.
- Keep restore behavior candidate-bounded and tenant-scoped.
- Keep provider changes limited to marker clear support over existing marker storage.
