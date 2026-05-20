# Tasks: Portability Primitives

**Input**: Design documents from `/specs/013-portability-primitives/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required by the feature specification's independent tests and success criteria.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the portability model and service locations without changing provider behavior.

- [X] T001 Create portability model folder in `src/core/Aster.Core/Models/Portability/`
- [X] T002 Create portability test folder in `test/Aster.Tests/Portability/`
- [X] T003 [P] Add public `IResourcePortabilityService` interface in `src/core/Aster.Core/Abstractions/IResourcePortabilityService.cs`
- [X] T004 [P] Add provider-facing `IResourcePortabilityStore` interface in `src/core/Aster.Core/Abstractions/IResourcePortabilityStore.cs`
- [X] T005 Confirm no new package dependencies are required in `src/core/Aster.Core/Aster.Core.csproj`, `src/persistence/Aster.Persistence.SqliteJson/Aster.Persistence.SqliteJson.csproj`, and `test/Aster.Tests/Aster.Tests.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared contracts, diagnostics, and registration used by every portability story.

- [X] T006 Add `PortableSnapshot`, `PortableSnapshotExportRequest`, and export scope enums in `src/core/Aster.Core/Models/Portability/PortableSnapshot.cs`
- [X] T007 Add `PortableDiagnostic`, `PortableDiagnosticSeverity`, and diagnostic code constants in `src/core/Aster.Core/Models/Portability/PortableDiagnostic.cs`
- [X] T008 Add `PortableImportOptions`, `PortableImportCollisionMode`, planned/actual import counts, `PortableIdentityMapping`, `PortableEntityKind`, `PortableIdentityMappingReason`, and `PortableImportStatus` in `src/core/Aster.Core/Models/Portability/PortableImportModels.cs`
- [X] T009 Add `PortableSnapshotExportResult`, `PortableSnapshotValidationResult`, `PortableImportPreview`, and `PortableImportResult` in `src/core/Aster.Core/Models/Portability/PortableResults.cs`
- [X] T010 Add `PortableStoreReadRequest`, `PortableStoreSnapshot`, and `PortableTargetState` in `src/core/Aster.Core/Models/Portability/PortableStoreModels.cs`
- [X] T011 Register the default portability service in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`

**Checkpoint**: Shared contracts compile and user story work can begin.

---

## Phase 3: User Story 1 - Export A Portable Snapshot (Priority: P1) MVP

**Goal**: Export selected definitions, resources, resource versions, and activation state into a self-contained portable snapshot.

**Independent Test**: Create definitions, resources, versions, and activation state; export selected scopes; verify included content, required references, counts, and skipped activation diagnostics.

### Tests for User Story 1

- [X] T012 [P] [US1] Add definition-only export tests in `test/Aster.Tests/Portability/PortabilityExportTests.cs`
- [X] T013 [P] [US1] Add selected-resource version scope export tests in `test/Aster.Tests/Portability/PortabilityExportTests.cs`
- [X] T014 [P] [US1] Add skipped activation diagnostic export tests in `test/Aster.Tests/Portability/PortabilityExportTests.cs`
- [X] T015 [P] [US1] Add SQLite JSON export round-trip tests in `test/Aster.Tests/SqliteJson/SqliteJsonPortabilityStoreTests.cs`

### Implementation for User Story 1

- [X] T016 [US1] Implement export scope validation in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T017 [US1] Implement snapshot export orchestration in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T018 [US1] Implement in-memory snapshot reads in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs`
- [X] T019 [US1] Implement SQLite JSON snapshot reads in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T020 [US1] Emit `skipped-activation-entry` diagnostics from export in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`

**Checkpoint**: User Story 1 exports valid snapshots and diagnostics independently.

---

## Phase 4: User Story 2 - Import With Deterministic Identity Mapping (Priority: P2)

**Goal**: Import snapshots safely with preserved identifiers, identical-content reuse, strict collision failures, and deterministic remap mode.

**Independent Test**: Import a snapshot into empty and colliding stores; verify relationships, identity map stability, no-op reuse, remap behavior, and all-or-nothing failure.

### Tests for User Story 2

- [X] T021 [P] [US2] Add empty-store import tests in `test/Aster.Tests/Portability/PortabilityImportTests.cs`
- [X] T022 [P] [US2] Add identical-content no-op import tests in `test/Aster.Tests/Portability/PortabilityImportTests.cs`
- [X] T023 [P] [US2] Add strict divergent collision failure tests in `test/Aster.Tests/Portability/PortabilityImportTests.cs`
- [X] T024 [P] [US2] Add deterministic remap import tests in `test/Aster.Tests/Portability/PortabilityImportTests.cs`
- [X] T025 [P] [US2] Add SQLite JSON atomic import tests in `test/Aster.Tests/SqliteJson/SqliteJsonPortabilityStoreTests.cs`

### Implementation for User Story 2

- [X] T026 [US2] Implement target-state comparison in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T027 [US2] Implement deterministic identity remapping in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T028 [US2] Implement relationship rewrites for remapped definitions, resources, resource versions, and activation state in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T029 [US2] Implement in-memory atomic import apply in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs`
- [X] T030 [US2] Implement SQLite JSON atomic import apply in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`

**Checkpoint**: User Story 2 imports valid snapshots and rejects unsafe imports independently.

---

## Phase 5: User Story 3 - Preview Import Diagnostics Before Writing (Priority: P3)

**Goal**: Preview import counts, identity mappings, and diagnostics without mutating target storage.

**Independent Test**: Preview valid, colliding, and invalid snapshots; verify diagnostics and counts while target stores remain unchanged.

### Tests for User Story 3

- [X] T031 [P] [US3] Add non-mutating preview tests in `test/Aster.Tests/Portability/PortabilityPreviewTests.cs`
- [X] T032 [P] [US3] Add invalid snapshot preview diagnostic tests in `test/Aster.Tests/Portability/PortabilityPreviewTests.cs`
- [X] T033 [P] [US3] Add preview identity-map determinism tests in `test/Aster.Tests/Portability/PortabilityPreviewTests.cs`

### Implementation for User Story 3

- [X] T034 [US3] Implement snapshot validation in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T035 [US3] Implement preview planning without provider writes in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T036 [US3] Share preview planning with write import in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`

**Checkpoint**: User Story 3 previews imports without mutation.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify the complete portability slice and update public guidance.

- [ ] T037 [P] Update `src/core/Aster.Core/README.md` with portability service usage
- [ ] T038 [P] Update `src/persistence/Aster.Persistence.SqliteJson/README.md` with provider portability support notes
- [ ] T039 Validate quickstart scenarios against implemented APIs in `specs/013-portability-primitives/quickstart.md`
- [ ] T040 Run `dotnet test Aster.sln` and fix portability regressions
- [ ] T041 Run `dotnet build Aster.sln /m:1`
- [ ] T042 Run `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; uses export fixtures from US1 tests when available.
- **User Story 3 (Phase 5)**: Depends on Foundational; shares import planning with US2.
- **Polish (Phase 6)**: Depends on completed target stories.

### User Story Dependencies

- **US1 Export A Portable Snapshot**: Can start after Foundational.
- **US2 Import With Deterministic Identity Mapping**: Can start after Foundational, but full end-to-end validation benefits from US1 export fixtures.
- **US3 Preview Import Diagnostics Before Writing**: Can start after Foundational and should share planning code with US2.

### Parallel Opportunities

- T003 and T004 can run in parallel after T001.
- T006 through T010 can be split across model files after interfaces are placed.
- US1 tests T012 through T015 can be drafted in parallel.
- US2 tests T021 through T025 can be drafted in parallel.
- US3 tests T031 through T033 can be drafted in parallel.
- README updates T037 and T038 can run in parallel after implementation stabilizes.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 shared contracts.
3. Complete Phase 3 export implementation and tests.
4. Validate with `dotnet test Aster.sln --filter PortabilityExport`.

### Incremental Delivery

1. Deliver export snapshots and skipped activation diagnostics.
2. Add write import with strict and remap modes.
3. Add preview diagnostics and share planning with write import.
4. Run full build, test, and whitespace validation.
