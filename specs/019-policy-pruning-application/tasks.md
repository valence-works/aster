# Tasks: Policy Pruning Application

**Input**: Design documents from `/specs/019-policy-pruning-application/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/policy-pruning-application.md, quickstart.md

**Tests**: Included because this feature is destructive and the spec requires independently testable stories with stable diagnostics.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify the active feature context and prepare shared documentation surfaces.

- [ ] T001 Confirm active feature context and plan reference in `.specify/feature.json` and `AGENTS.md`
- [ ] T002 [P] Review existing policy preview/application tests for reusable fixture patterns in `test/Aster.Tests/Policies/`
- [ ] T003 [P] Review existing resource store deletion constraints in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs` and `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared contracts, models, diagnostics, and DI hooks needed by every user story.

**CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 Add `IResourcePolicyPruningApplicationService` and `IResourceVersionPruningStore` contracts in `src/core/Aster.Core/Abstractions/IResourcePolicyPruningApplicationService.cs`
- [ ] T005 Add `ResourcePolicyPruningApplicationRequest`, `ResourcePolicyPruningApplicationCandidate`, `ResourcePolicyPruningApplicationResult`, `ResourcePolicyPruningApplicationCandidateResult`, and `ResourcePolicyPruningApplicationCandidateStatus` in `src/core/Aster.Core/Models/Policies/ResourcePolicyPruningApplication.cs`
- [ ] T006 Add stable pruning diagnostic codes in `src/core/Aster.Core/Models/Policies/ResourcePolicyResults.cs`
- [ ] T007 Register core pruning services and in-memory pruning capability in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [ ] T008 Register SQLite JSON pruning capability in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [ ] T009 Add shared policy pruning test fixtures in `test/Aster.Tests/Policies/PolicyPruningApplicationTestFixtures.cs`

**Checkpoint**: Contracts, models, diagnostics, DI registrations, and test fixtures are available.

---

## Phase 3: User Story 1 - Preview-Selected Pruning Application (Priority: P1) MVP

**Goal**: Hosts can explicitly apply selected pruning preview candidates and remove only selected eligible historical inactive versions.

**Independent Test**: Create resources with multiple versions, generate or construct pruning preview candidates, apply a subset, and verify only selected eligible versions are removed.

### Tests for User Story 1

- [ ] T010 [P] [US1] Add successful subset pruning tests in `test/Aster.Tests/Policies/PolicyPruningApplicationServiceTests.cs`
- [ ] T011 [P] [US1] Add result aggregate and empty/null candidate list tests in `test/Aster.Tests/Policies/PolicyPruningApplicationResultTests.cs`
- [ ] T012 [P] [US1] Add in-memory provider removal tests in `test/Aster.Tests/Policies/PolicyPruningApplicationStoreTests.cs`

### Implementation for User Story 1

- [ ] T013 [US1] Implement `IResourceVersionPruningStore` in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs`
- [ ] T014 [US1] Implement `ResourcePolicyPruningApplicationService` happy path in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T015 [US1] Add candidate shape validation, null candidate list handling, and aggregate result helpers in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T016 [US1] Ensure selected-candidate pruning does not remove unselected versions in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T017 [US1] Register and resolve `IResourcePolicyPruningApplicationService` through `AddAsterCore()` in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - Fail-Closed Safety Preflight (Priority: P2)

**Goal**: Pruning application rechecks current state before destructive removal and fails closed for stale or unsafe candidates.

**Independent Test**: Preview or construct candidates, change resource/policy/activation/lifecycle state before application, and verify candidates fail with stable diagnostics and no protected version is removed.

### Tests for User Story 2

- [ ] T018 [P] [US2] Add latest-version and active-version protection tests in `test/Aster.Tests/Policies/PolicyPruningApplicationSafetyTests.cs`
- [ ] T019 [P] [US2] Add policy missing, policy mismatch, and criteria mismatch tests in `test/Aster.Tests/Policies/PolicyPruningApplicationSafetyTests.cs`
- [ ] T020 [P] [US2] Add retained-version unsafe removal tests in `test/Aster.Tests/Policies/PolicyPruningApplicationSafetyTests.cs`
- [ ] T021 [P] [US2] Add provider unsupported and provider write failure tests in `test/Aster.Tests/Policies/PolicyPruningApplicationDiagnosticsTests.cs`

### Implementation for User Story 2

- [ ] T022 [US2] Add policy declaration and criteria revalidation in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T023 [US2] Add latest-version and active-version protection in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T024 [US2] Add lifecycle marker criteria revalidation in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T025 [US2] Add retained-version safety floor validation in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T026 [US2] Add provider unsupported and write-failed diagnostic handling in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`

**Checkpoint**: User Story 2 fails closed for stale, protected, unsafe, and unsupported candidates.

---

## Phase 5: User Story 3 - Tenant-Bounded Deterministic Results (Priority: P3)

**Goal**: Pruning application operates inside one effective tenant and produces deterministic outcomes for duplicates and already-pruned versions.

**Independent Test**: Create matching identifiers in two tenants, apply pruning in one tenant, verify the other tenant remains unchanged, then retry duplicate and already-pruned candidates.

### Tests for User Story 3

- [ ] T027 [P] [US3] Add tenant isolation pruning tests in `test/Aster.Tests/Tenancy/TenantPolicyPruningApplicationTests.cs`
- [ ] T028 [P] [US3] Add duplicate candidate and already-pruned retry tests in `test/Aster.Tests/Policies/PolicyPruningApplicationResultTests.cs`
- [ ] T029 [P] [US3] Add SQLite JSON tenant-scoped pruning tests in `test/Aster.Tests/SqliteJson/SqliteJsonPolicyPruningApplicationTests.cs`

### Implementation for User Story 3

- [ ] T030 [US3] Add deterministic duplicate handling in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T031 [US3] Add already-pruned result handling in `src/core/Aster.Core/Services/ResourcePolicyPruningApplicationService.cs`
- [ ] T032 [US3] Implement tenant-scoped SQLite JSON version pruning in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [ ] T033 [US3] Verify SQLite JSON pruning leaves definitions, activation, lifecycle markers, and other tenant versions unchanged in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`

**Checkpoint**: User Story 3 is tenant-safe and deterministic.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, compatibility tests, cleanup, and final validation.

- [ ] T034 [P] Update core SDK documentation for policy pruning application in `src/core/Aster.Core/README.md`
- [ ] T035 [P] Update SQLite JSON provider documentation for version pruning support in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [ ] T036 [P] Update roadmap status for slice 019 in `docs/ExecutionRoadmap.md`
- [ ] T037 [P] Add portability regression proving pruned versions are absent from export without format changes in `test/Aster.Tests/Portability/PortabilityPolicyPruningTests.cs`
- [ ] T038 Run quickstart verification against `specs/019-policy-pruning-application/quickstart.md`
- [ ] T039 Run `dotnet test Aster.sln`
- [ ] T040 Run `dotnet build Aster.sln /m:1`
- [ ] T041 Run `git diff --check`
- [ ] T042 Re-run Constitution Check and remove unnecessary abstractions or speculative behavior before final handoff

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 Setup has no dependencies.
- Phase 2 Foundational depends on Phase 1 and blocks all user stories.
- Phase 3 User Story 1 depends on Phase 2 and is the MVP.
- Phase 4 User Story 2 depends on Phase 3 service structure but remains independently testable through safety scenarios.
- Phase 5 User Story 3 depends on Phase 3 service structure and Phase 2 provider contracts.
- Phase 6 Polish depends on desired user stories being complete.

### User Story Dependencies

- **US1**: Requires foundational contracts and models only.
- **US2**: Builds on US1 service structure to add fail-closed preflight.
- **US3**: Builds on US1 service structure to add tenant, duplicate, retry, and SQLite provider behavior.

### Parallel Opportunities

- T002 and T003 can run in parallel.
- T010, T011, and T012 can run in parallel after Phase 2.
- T018, T019, T020, and T021 can run in parallel.
- T027, T028, and T029 can run in parallel.
- T034, T035, T036, and T037 can run in parallel after implementation.

---

## Parallel Example: User Story 2

```text
Task: "T018 [P] [US2] Add latest-version and active-version protection tests in test/Aster.Tests/Policies/PolicyPruningApplicationSafetyTests.cs"
Task: "T019 [P] [US2] Add policy missing, policy mismatch, and criteria mismatch tests in test/Aster.Tests/Policies/PolicyPruningApplicationSafetyTests.cs"
Task: "T020 [P] [US2] Add retained-version unsafe removal tests in test/Aster.Tests/Policies/PolicyPruningApplicationSafetyTests.cs"
Task: "T021 [P] [US2] Add provider unsupported and provider write failure tests in test/Aster.Tests/Policies/PolicyPruningApplicationDiagnosticsTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 for preview-selected pruning application.
3. Validate selected eligible versions are removed and unselected versions remain.

### Incremental Delivery

1. Add foundational contracts/models/DI.
2. Add US1 selected pruning application.
3. Add US2 stale/protected/unsafe fail-closed behavior.
4. Add US3 tenant, duplicate, retry, and SQLite provider behavior.
5. Complete documentation and full validation.

### Guardrails

- Do not add automatic retention execution, schedulers, authorization engines, provider registries, public SQL, public queryable resource surfaces, broad workflow/state-machine infrastructure, or schema migrations.
- Do not mutate activation state, lifecycle markers, definitions, policy declarations, or remaining resource versions during pruning.
- Keep destructive behavior explicit, candidate-bounded, tenant-scoped, and observable through per-candidate results.
