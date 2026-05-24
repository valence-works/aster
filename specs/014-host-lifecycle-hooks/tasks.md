# Tasks: Host Lifecycle Hooks

**Input**: Design documents from `/specs/014-host-lifecycle-hooks/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required by the feature specification's independent tests and measurable success criteria.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish lifecycle hook source and test locations without changing runtime behavior.

- [X] T001 Create lifecycle model folder in `src/core/Aster.Core/Models/Lifecycle/`
- [X] T002 Create lifecycle test folder in `test/Aster.Tests/Lifecycle/`
- [X] T003 [P] Add lifecycle hook test fixture helpers in `test/Aster.Tests/Lifecycle/LifecycleHookTestFixtures.cs`
- [X] T004 Confirm no new package dependencies are required in `src/core/Aster.Core/Aster.Core.csproj` and `test/Aster.Tests/Aster.Tests.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared contracts, outcomes, contexts, coordinator, and registration used by every lifecycle hook story.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 [P] Add lifecycle point and save kind enums in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookEnums.cs`
- [X] T006 [P] Add `LifecycleHookOutcome` and outcome status models in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookOutcome.cs`
- [X] T007 [P] Add `LifecycleHookDiagnostic` model in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookDiagnostic.cs`
- [X] T008 [P] Add `LifecycleHookException` in `src/core/Aster.Core/Exceptions/LifecycleHookException.cs`
- [X] T009 [P] Add immutable save, activation, export, and import lifecycle context records in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookContexts.cs`
- [X] T010 Add `IResourceLifecycleHook` contract and no-op `ResourceLifecycleHook` base class in `src/core/Aster.Core/Abstractions/IResourceLifecycleHook.cs`
- [X] T011 Add `IResourceLifecycleHookDispatcher` contract in `src/core/Aster.Core/Abstractions/IResourceLifecycleHookDispatcher.cs`
- [X] T012 Implement deterministic hook dispatch, rejection, exception wrapping, and cancellation handling in `src/core/Aster.Core/Services/ResourceLifecycleHookDispatcher.cs`
- [X] T013 Register `ResourceLifecycleHookDispatcher` and add `AddResourceLifecycleHook<THook>()` in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T014 [P] Add dispatcher ordering/rejection/cancellation tests in `test/Aster.Tests/Lifecycle/ResourceLifecycleHookDispatcherTests.cs`

**Checkpoint**: Shared hook contracts compile and user story work can begin.

---

## Phase 3: User Story 1 - Run Save Hooks Around Resource Mutations (Priority: P1) MVP

**Goal**: Run explicit before/after save hooks around create, update, and explicit schema-upgrade saves.

**Independent Test**: Register two hooks, save resources through create/update/schema-upgrade, and verify order, context, rejection, cancellation, after-success behavior, and no change to unrelated query or storage behavior.

### Tests for User Story 1

- [X] T015 [P] [US1] Add create/update save hook ordering and context tests in `test/Aster.Tests/Lifecycle/LifecycleSaveHookTests.cs`
- [X] T016 [P] [US1] Add before-save rejection prevents create/update persistence tests in `test/Aster.Tests/Lifecycle/LifecycleSaveHookTests.cs`
- [X] T017 [P] [US1] Add after-save failure visibility tests in `test/Aster.Tests/Lifecycle/LifecycleSaveHookTests.cs`
- [X] T018 [P] [US1] Add schema-upgrade save hook tests in `test/Aster.Tests/Lifecycle/LifecycleSchemaUpgradeHookTests.cs`

### Implementation for User Story 1

- [X] T019 [US1] Inject `IResourceLifecycleHookDispatcher` into `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T020 [US1] Invoke before-save and after-save hooks around `CreateAsync` in `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T021 [US1] Invoke before-save and after-save hooks around `UpdateAsync` in `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T022 [US1] Inject `IResourceLifecycleHookDispatcher` into `src/core/Aster.Core/Services/ResourceSchemaVersionService.cs`
- [X] T023 [US1] Invoke before-save and after-save hooks around schema-upgrade writes in `src/core/Aster.Core/Services/ResourceSchemaVersionService.cs`

**Checkpoint**: User Story 1 save hooks work independently.

---

## Phase 4: User Story 2 - Run Activation Hooks Around Channel Changes (Priority: P2)

**Goal**: Run explicit before/after activation and deactivation hooks around channel state changes.

**Independent Test**: Register activation/deactivation hooks, activate and deactivate resource versions, and verify context, order, rejection, cancellation, and after-success behavior independently of save hooks.

### Tests for User Story 2

- [X] T024 [P] [US2] Add activation hook ordering and context tests in `test/Aster.Tests/Lifecycle/LifecycleActivationHookTests.cs`
- [X] T025 [P] [US2] Add before-activation rejection leaves channel state unchanged tests in `test/Aster.Tests/Lifecycle/LifecycleActivationHookTests.cs`
- [X] T026 [P] [US2] Add deactivation hook ordering and context tests in `test/Aster.Tests/Lifecycle/LifecycleDeactivationHookTests.cs`
- [X] T027 [P] [US2] Add before-deactivation rejection leaves channel state unchanged tests in `test/Aster.Tests/Lifecycle/LifecycleDeactivationHookTests.cs`

### Implementation for User Story 2

- [X] T028 [US2] Invoke before-activation and after-activation hooks around `ActivateAsync` in `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T029 [US2] Include `AllowMultipleActive` and resulting active version data in activation hook contexts in `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T030 [US2] Invoke before-deactivation and after-deactivation hooks around `DeactivateAsync` in `src/core/Aster.Core/Services/DefaultResourceManager.cs`

**Checkpoint**: User Story 2 activation hooks work independently alongside User Story 1.

---

## Phase 5: User Story 3 - Run Portability Hooks Around Export And Import (Priority: P3)

**Goal**: Run explicit before/after export, import preview, and write import hooks with diagnostics aligned to portability results.

**Independent Test**: Register portability hooks, export and preview/import snapshots, and verify hook order, snapshot/import context, rejection diagnostics, cancellation, and non-mutation behavior for preview operations.

### Tests for User Story 3

- [X] T031 [P] [US3] Add export hook ordering/context and rejection diagnostic tests in `test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs`
- [X] T032 [P] [US3] Add preview-import hook ordering/context and non-mutation tests in `test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs`
- [X] T033 [P] [US3] Add write-import hook ordering/context and after-success tests in `test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs`
- [X] T034 [P] [US3] Add portability hook failure diagnostic tests in `test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs`

### Implementation for User Story 3

- [X] T035 [US3] Add lifecycle hook diagnostic codes to `src/core/Aster.Core/Models/Portability/PortableDiagnostic.cs`
- [X] T036 [US3] Inject `IResourceLifecycleHookDispatcher` into `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T037 [US3] Invoke before-export and after-export hooks in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T038 [US3] Invoke before-preview-import and after-preview-import hooks in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T039 [US3] Invoke before-import and after-import hooks in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T040 [US3] Map portability hook rejections and failures to portability diagnostics in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`

**Checkpoint**: User Story 3 portability hooks work independently alongside resource lifecycle hooks.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify the complete hook slice, update guidance, and preserve existing no-hook behavior.

- [X] T041 [P] Update lifecycle hook usage documentation in `src/core/Aster.Core/README.md`
- [X] T042 [P] Validate quickstart examples against implemented APIs in `specs/014-host-lifecycle-hooks/quickstart.md`
- [X] T043 Add no-hook compatibility regression coverage in `test/Aster.Tests/Lifecycle/LifecycleHookCompatibilityTests.cs`
- [X] T044 Run focused lifecycle and portability hook tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~Lifecycle|FullyQualifiedName~Portability"`
- [X] T045 Run `dotnet test Aster.sln --no-restore`
- [X] T046 Run `dotnet build Aster.sln /m:1`
- [X] T047 Run focused `dotnet format Aster.sln --verify-no-changes --include` for changed source and test files
- [X] T048 Run `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; can be implemented independently after dispatcher contracts exist.
- **User Story 3 (Phase 5)**: Depends on Foundational; can be implemented independently after dispatcher contracts exist.
- **Polish (Phase 6)**: Depends on completed target stories.

### User Story Dependencies

- **US1 Save Hooks**: Can start after Foundational.
- **US2 Activation Hooks**: Can start after Foundational; no dependency on US1 implementation except shared dispatcher.
- **US3 Portability Hooks**: Can start after Foundational; no dependency on US1/US2 implementation except shared dispatcher.

### Parallel Opportunities

- T003 and T004 can run in parallel after T001 and T002.
- T005 through T009 can run in parallel after folders exist.
- T014 can be written after T011 and before service integrations.
- US1 tests T015 through T018 can be drafted in parallel.
- US2 tests T024 through T027 can be drafted in parallel.
- US3 tests T031 through T034 can be drafted in parallel.
- Documentation tasks T041 and T042 can run in parallel after APIs stabilize.

---

## Parallel Example: User Story 1

```bash
# Draft save-hook tests together:
Task: "Add create/update save hook ordering and context tests in test/Aster.Tests/Lifecycle/LifecycleSaveHookTests.cs"
Task: "Add schema-upgrade save hook tests in test/Aster.Tests/Lifecycle/LifecycleSchemaUpgradeHookTests.cs"

# Then integrate save hook dispatch sequentially:
Task: "Invoke before-save and after-save hooks around CreateAsync in src/core/Aster.Core/Services/DefaultResourceManager.cs"
Task: "Invoke before-save and after-save hooks around UpdateAsync in src/core/Aster.Core/Services/DefaultResourceManager.cs"
```

## Parallel Example: User Story 2

```bash
# Draft activation/deactivation tests together:
Task: "Add activation hook ordering and context tests in test/Aster.Tests/Lifecycle/LifecycleActivationHookTests.cs"
Task: "Add deactivation hook ordering and context tests in test/Aster.Tests/Lifecycle/LifecycleDeactivationHookTests.cs"
```

## Parallel Example: User Story 3

```bash
# Draft portability hook tests together:
Task: "Add export hook ordering/context and rejection diagnostic tests in test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs"
Task: "Add preview-import hook ordering/context and non-mutation tests in test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs"
Task: "Add write-import hook ordering/context and after-success tests in test/Aster.Tests/Portability/PortabilityLifecycleHookTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 shared hook contracts, models, dispatcher, and DI.
3. Complete Phase 3 save hook implementation and tests.
4. Validate with focused lifecycle tests before adding activation or portability hooks.

### Incremental Delivery

1. Deliver explicit save hooks around create, update, and schema upgrade.
2. Add activation/deactivation hooks using the same dispatcher and context model.
3. Add portability export/preview/import hooks with diagnostics mapped to existing result objects.
4. Run full build, test, focused format, and whitespace validation.

### Parallel Team Strategy

With multiple implementers:

1. Complete Setup and Foundational tasks together.
2. Split US1, US2, and US3 after the dispatcher and context records are available.
3. Keep edits to disjoint service/test files where possible; coordinate on shared model/contract changes.

## Notes

- [P] tasks use different files or can be drafted without depending on incomplete implementation.
- [US1], [US2], and [US3] labels map to the user stories in `spec.md`.
- Each user story is independently testable after the foundational dispatcher is complete.
- Avoid runtime scanning, hidden provider discovery, recipes, durable event delivery, public SQL, and public `IQueryable<Resource>`.
