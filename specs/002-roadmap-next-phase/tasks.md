# Tasks: Persistence & Querying Essentials (Phase 2)

**Input**: Design documents from `/specs/002-roadmap-next-phase/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included because the specification defines measurable validation outcomes (SC-001..SC-005) and independent test criteria per user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create provider project skeleton and wire solution-level prerequisites.

- [ ] T001 Add provider project file at src/providers/Aster.Persistence.Sqlite/Aster.Persistence.Sqlite.csproj
- [ ] T002 Add provider project to solution in Aster.sln
- [ ] T003 [P] Create provider folder structure and placeholder files under src/providers/Aster.Persistence.Sqlite/{Extensions,Infrastructure,Stores,Query,Serialization,Diagnostics}
- [ ] T004 [P] Add SQLite package references in src/providers/Aster.Persistence.Sqlite/Aster.Persistence.Sqlite.csproj

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build core provider plumbing required before implementing any user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T005 Define provider options contract (including `SlowQueryThreshold`, default 500 ms) in src/providers/Aster.Persistence.Sqlite/Configuration/SqlitePersistenceOptions.cs
- [ ] T005-a Implement structured logging helpers (lifecycle events, concurrency conflicts, slow-query detection) in src/providers/Aster.Persistence.Sqlite/Diagnostics/SqliteProviderLogger.cs
- [ ] T006 [P] Implement SQLite connection factory in src/providers/Aster.Persistence.Sqlite/Infrastructure/SqliteConnectionFactory.cs
- [ ] T007 [P] Implement JSON serialization helper for persisted payloads in src/providers/Aster.Persistence.Sqlite/Serialization/SqliteJsonSerializer.cs
- [ ] T008 Define shared persistence record mappings in src/providers/Aster.Persistence.Sqlite/Models/PersistenceRecords.cs
- [ ] T008-a Add `ChannelMode` enum to src/core/Aster.Core/Models/Instances/ChannelMode.cs
- [ ] T008-b Add `Mode` property (`ChannelMode`) to `ActivationState` in src/core/Aster.Core/Models/Instances/ActivationState.cs
- [ ] T008-c Update `IResourceManager.ActivateAsync` signature: replace `bool allowMultipleActive` with `ChannelMode? mode` in src/core/Aster.Core/Abstractions/IResourceManager.cs and update InMemoryResourceManager accordingly
- [ ] T009 Implement provider DI registration extensions in src/providers/Aster.Persistence.Sqlite/Extensions/ServiceCollectionExtensions.cs
- [ ] T010 Wire provider selection/configuration in src/apps/Aster.Web/Program.cs
- [ ] T011 Add SQLite connection settings in src/apps/Aster.Web/appsettings.json
- [ ] T012 [P] Add development SQLite settings in src/apps/Aster.Web/appsettings.Development.json
- [ ] T013 Add baseline provider bootstrapping integration test scaffold in test/Aster.Tests/Integration/SqliteProviderBootstrapTests.cs

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Durable Resource Lifecycle (Priority: P1) 🎯 MVP

**Goal**: Persist definitions/resources/versions/activation across restarts while preserving append-only history and optimistic concurrency.

**Independent Test**: Create and update resources, activate versions, restart host/store, and verify latest/historical/active retrieval is unchanged.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add append-only persistence tests in test/Aster.Tests/Persistence/SqliteResourceWriteStoreTests.cs
- [ ] T015 [P] [US1] Add optimistic concurrency conflict tests in test/Aster.Tests/Persistence/SqliteConcurrencyTests.cs
- [ ] T016 [P] [US1] Add activation mode tests (SingleActive vs MultiActive, mode persistence and enforcement) in test/Aster.Tests/Persistence/SqliteActivationModeTests.cs
- [ ] T017 [P] [US1] Add restart durability integration test (versions, activation state, and stored ChannelMode — covers SC-001 and SC-005) in test/Aster.Tests/Integration/SqliteDurabilityIntegrationTests.cs

### Implementation for User Story 1

- [ ] T018 [US1] Implement ResourceDefinitionRecord persistence store in src/providers/Aster.Persistence.Sqlite/Stores/SqliteResourceDefinitionStore.cs
- [ ] T019 [US1] Implement resource version write persistence in src/providers/Aster.Persistence.Sqlite/Stores/SqliteResourceWriteStore.cs
- [ ] T020 [US1] Implement ActivationRecord persistence (upsert/read activation state + durable ChannelMode per ResourceId + channel) in src/providers/Aster.Persistence.Sqlite/Stores/SqliteActivationStore.cs
- [ ] T021 [US1] Implement transactional optimistic concurrency checks in src/providers/Aster.Persistence.Sqlite/Stores/SqliteResourceTransactionCoordinator.cs
- [ ] T022 [US1] Implement typed error mapping for persistence conflicts/not-found in src/providers/Aster.Persistence.Sqlite/Diagnostics/SqliteErrorMapper.cs
- [ ] T023 [US1] Add provider-backed resource manager integration wiring in src/providers/Aster.Persistence.Sqlite/Extensions/ServiceCollectionExtensions.cs
- [ ] T024 [US1] Add host seed path for persisted lifecycle smoke data in src/apps/Aster.Web/SeedDataInitializer.cs

**Checkpoint**: User Story 1 is independently functional and testable (MVP).

---

## Phase 4: User Story 2 - Persistent Querying for Operational Use (Priority: P2)

**Goal**: Execute persisted metadata/aspect queries with equals/contains/range plus deterministic paging/sorting and missing-value-last semantics.

**Independent Test**: Seed mixed records and verify query filtering, sorted pagination, and correctness targets against persisted data.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add query operator behavior tests (equals/contains/range) in test/Aster.Tests/Persistence/SqliteQueryOperatorTests.cs
- [ ] T026 [P] [US2] Add deterministic paging/sorting tests in test/Aster.Tests/Persistence/SqliteQueryPagingSortingTests.cs
- [ ] T027 [P] [US2] Add missing-sort-value-last tests in test/Aster.Tests/Persistence/SqliteQueryNullSortTests.cs
- [ ] T028 [P] [US2] Add 100k dataset query performance integration test in test/Aster.Tests/Integration/SqliteQueryPerformanceIntegrationTests.cs

### Implementation for User Story 2

- [ ] T029 [US2] Implement ResourceQuery AST to SQL translator in src/providers/Aster.Persistence.Sqlite/Query/SqliteQueryTranslator.cs
- [ ] T030 [US2] Implement query command builder with parameterization and tie-break sorting in src/providers/Aster.Persistence.Sqlite/Query/SqliteQueryCommandBuilder.cs
- [ ] T031 [US2] Implement missing-sort-value-last strategy in src/providers/Aster.Persistence.Sqlite/Query/SqliteSortSemantics.cs
- [ ] T032 [US2] Implement SQLite-backed query service in src/providers/Aster.Persistence.Sqlite/Stores/SqliteResourceQueryService.cs
- [ ] T033 [US2] Add query validation and unsupported-feature errors in src/providers/Aster.Persistence.Sqlite/Diagnostics/SqliteQueryValidation.cs

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story completion, documentation, and final verification.

- [ ] T034 [P] Update provider usage and configuration docs in README.md
- [ ] T035 [P] Add persistence architecture notes in docs/architecture-review.md
- [ ] T036 Run full quickstart validation scenarios from specs/002-roadmap-next-phase/quickstart.md
- [ ] T037 Run complete test suite and capture SC-001..SC-005 evidence in specs/002-roadmap-next-phase/quickstart.md
- [ ] T038 Final code cleanup/refactor across src/providers/Aster.Persistence.Sqlite/**/*.cs

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 completion and blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2 completion; delivers MVP.
- **Phase 4 (US2)**: Depends on Phase 2 completion; can proceed after or alongside US1 if staffed.
- **Phase 5 (Polish)**: Depends on completion of selected user stories.

### User Story Dependencies

- **US1 (P1)**: No dependency on other user stories.
- **US2 (P2)**: Independent of US1, but reuses foundational provider plumbing.

### Within Each User Story

- Tests first (expected to fail initially).
- Data/store primitives before service wiring.
- Service wiring before host integration.
- Story checkpoint must pass before declaring story complete.

### Parallel Opportunities

- Phase 1: T003, T004 can run in parallel.
- Phase 2: T006, T007, T012 can run in parallel after T005.
- US1: T014–T017 can run in parallel; T018 and T019 can run in parallel before integration tasks.
- US2: T025–T028 can run in parallel; T029 and T031 can run in parallel before T030/T032.

---

## Parallel Example: User Story 1

```bash
Task: "T014 [US1] Add append-only persistence tests in test/Aster.Tests/Persistence/SqliteResourceWriteStoreTests.cs"
Task: "T015 [US1] Add optimistic concurrency conflict tests in test/Aster.Tests/Persistence/SqliteConcurrencyTests.cs"
Task: "T016 [US1] Add activation mode tests in test/Aster.Tests/Persistence/SqliteActivationModeTests.cs"
Task: "T017 [US1] Add restart durability integration test in test/Aster.Tests/Integration/SqliteDurabilityIntegrationTests.cs"
```

## Parallel Example: User Story 2

```bash
Task: "T025 [US2] Add query operator behavior tests in test/Aster.Tests/Persistence/SqliteQueryOperatorTests.cs"
Task: "T026 [US2] Add deterministic paging/sorting tests in test/Aster.Tests/Persistence/SqliteQueryPagingSortingTests.cs"
Task: "T027 [US2] Add missing-sort-value-last tests in test/Aster.Tests/Persistence/SqliteQueryNullSortTests.cs"
Task: "T028 [US2] Add 100k dataset query performance integration test in test/Aster.Tests/Integration/SqliteQueryPerformanceIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate US1 independently via durability and concurrency tests.
4. Demo/deploy MVP if ready.

### Incremental Delivery

1. Deliver US1 (durable lifecycle).
2. Deliver US2 (persistent querying).
3. Deliver US3 (infrastructure readiness).
4. Run polish phase and full quickstart validation.

### Parallel Team Strategy

1. Team aligns on Setup + Foundational tasks.
2. Then split by stories:
   - Engineer A: US1
   - Engineer B: US2
   - Engineer C: US3
3. Rejoin for Phase 6 polish and final verification.

---

## Notes

- All tasks use strict checklist format: `- [ ] T### [P?] [US?] Description with file path`.
- `[US#]` labels appear only in user story phases.
- Each user story includes explicit independent test criteria and implementation tasks.
- Avoid cross-story coupling beyond foundational shared infrastructure.
