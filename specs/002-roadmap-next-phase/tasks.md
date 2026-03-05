# Tasks: Persistence & Querying Essentials (Phase 2)

**Input**: Design documents from `/specs/002-roadmap-next-phase/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included because the specification defines measurable validation outcomes (SC-001..SC-005) and independent test criteria per user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the `Aster.Persistence.Sqlite` provider project skeleton and register it in the solution.

- [ ] T001 Create provider project file with multi-target `net8.0;net9.0;net10.0` and reference to Aster.Core at src/persistence/Aster.Persistence.Sqlite/Aster.Persistence.Sqlite.csproj
- [ ] T002 Add Aster.Persistence.Sqlite project reference to Aster.sln
- [ ] T003 [P] Create directory structure under src/persistence/Aster.Persistence.Sqlite/{Extensions,Persistence,Schema,Internal}
- [ ] T004 [P] Add `Microsoft.Data.Sqlite` and `Microsoft.Extensions.Logging.Abstractions` package references to src/persistence/Aster.Persistence.Sqlite/Aster.Persistence.Sqlite.csproj

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add `ChannelMode` to `Aster.Core`, update `IResourceManager`, and build shared Sqlite provider infrastructure required before any user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T005 Add `ChannelMode` enum (`SingleActive` | `MultiActive`) to src/core/Aster.Core/Models/Instances/ChannelMode.cs
- [ ] T006 Add `Mode` property (`ChannelMode`, required) to `ActivationState` in src/core/Aster.Core/Models/Instances/ActivationState.cs
- [ ] T007 Update `IResourceManager.ActivateAsync` signature replacing `bool allowMultipleActive` with `ChannelMode? mode` in src/core/Aster.Core/Abstractions/IResourceManager.cs
- [ ] T008 Update `InMemoryResourceManager.ActivateAsync` to implement `ChannelMode` mode semantics and store mode on `ActivationState` in src/core/Aster.Core/InMemory/InMemoryResourceManager.cs
- [ ] T009 Update existing in-memory activation tests for the new `ChannelMode` API in test/Aster.Tests/InMemory/InMemoryActivationTests.cs
- [ ] T010 [P] Define `SqlitePersistenceOptions` with `SlowQueryThreshold` (default 500 ms) and connection configuration in src/persistence/Aster.Persistence.Sqlite/SqlitePersistenceOptions.cs
- [ ] T011 [P] Implement `System.Text.Json` serializer configuration for persisted payloads (`PayloadJson`, `AspectsJson`, `ActiveVersionsJson`) in src/persistence/Aster.Persistence.Sqlite/Internal/JsonSerializerOptions.cs
- [ ] T012 Implement `SchemaInitializer` to create `ResourceDefinitionRecord`, `ResourceRecord`, and `ActivationRecord` tables on first run in src/persistence/Aster.Persistence.Sqlite/Schema/SchemaInitializer.cs
- [ ] T013 Implement `AddSqlitePersistence()` DI extension registering all Sqlite provider services and running schema initialization in src/persistence/Aster.Persistence.Sqlite/Extensions/ServiceCollectionExtensions.cs
- [ ] T014 Wire Sqlite provider in host composition root at src/apps/Aster.Web/Program.cs
- [ ] T015 [P] Add Sqlite connection string configuration to src/apps/Aster.Web/appsettings.json
- [ ] T016 [P] Add development Sqlite connection overrides to src/apps/Aster.Web/appsettings.Development.json

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Durable Resource Lifecycle (Priority: P1) 🎯 MVP

**Goal**: Persist resource definitions, versions, and activation state across restarts while preserving append-only history, optimistic concurrency, and durable `ChannelMode` enforcement.

**Independent Test**: Create definitions and multi-version resources, activate versions with explicit `ChannelMode`, restart the host, and verify all versions and channel activation state are unchanged and retrievable.

### Tests for User Story 1

- [ ] T017 [P] [US1] Add definition registration and round-trip retrieval tests (latest, specific version, list) in test/Aster.Tests/Persistence/SqliteDefinitionStoreTests.cs
- [ ] T018 [P] [US1] Add resource append-only write, version retrieval, and IsSingleton enforcement tests in test/Aster.Tests/Persistence/SqliteResourceWriteStoreTests.cs
- [ ] T019 [P] [US1] Add `ActivationRecord` persistence, durable `ChannelMode`, and mode-enforcement tests (`SingleActive` vs `MultiActive`) in test/Aster.Tests/Persistence/SqliteActivationTests.cs
- [ ] T020 [P] [US1] Add concurrent save/activate conflict tests verifying unbroken version history and typed `ConcurrencyConflict` outcome in test/Aster.Tests/Persistence/SqliteConcurrencyTests.cs
- [ ] T021 [P] [US1] Add restart durability integration test verifying all versions, activation state, and stored `ChannelMode` survive process restart (SC-001, SC-005) in test/Aster.Tests/Persistence/RestartDurabilityTests.cs

### Implementation for User Story 1

- [ ] T022 [US1] Implement `SqliteResourceDefinitionStore` (`RegisterDefinitionAsync` append-only with auto-increment version, `GetDefinitionAsync`, `GetDefinitionVersionAsync`, `ListDefinitionsAsync`, structured logging) in src/persistence/Aster.Persistence.Sqlite/Persistence/SqliteResourceDefinitionStore.cs
- [ ] T023 [US1] Implement `SqliteResourceWriteStore` (`SaveVersionAsync` with append-only, `IsSingleton` guard, optimistic concurrency on `BaseVersion`; `UpdateActivationAsync` with durable `ChannelMode` upsert and mode enforcement; version/activation read operations; structured logging) in src/persistence/Aster.Persistence.Sqlite/Persistence/SqliteResourceWriteStore.cs
- [ ] T024 [US1] Update `QuickstartIntegrationTest` to use the Sqlite provider and the new `ChannelMode` parameter in test/Aster.Tests/Integration/QuickstartIntegrationTest.cs
- [ ] T025 [US1] Update `SeedDataInitializer` to pass `ChannelMode.SingleActive` on all `ActivateAsync` calls in src/apps/Aster.Web/SeedDataInitializer.cs

**Checkpoint**: User Story 1 is independently functional and testable (MVP).

---

## Phase 4: User Story 2 - Persistent Querying for Operational Use (Priority: P2)

**Goal**: Translate `ResourceQuery` AST to parameterised Sqlite SQL supporting `Equals`/`Contains`/`Range` operators, deterministic paging and sorting, and missing-value-last sort semantics.

**Independent Test**: Seed mixed resource records (including some missing the sort field) and verify filter correctness, stable sorted paging, and missing-sort-value-last ordering against persisted data.

### Tests for User Story 2

- [ ] T026 [P] [US2] Add query filter operator tests (`Equals`, `Contains`, `Range`) on resource metadata and aspect values in test/Aster.Tests/Persistence/SqliteQueryOperatorTests.cs
- [ ] T027 [P] [US2] Add deterministic paging and sorting tests including tie-break on (`ResourceId`, `Version`) in test/Aster.Tests/Persistence/SqliteQueryPagingSortingTests.cs
- [ ] T028 [P] [US2] Add missing-sort-value-last tests asserting records without the sort field appear after all records with the field present in test/Aster.Tests/Persistence/SqliteQueryNullSortTests.cs
- [ ] T029 [P] [US2] Add 100k-version query performance integration test capturing SC-002 and SC-003 evidence in test/Aster.Tests/Persistence/PerformanceTests.cs

### Implementation for User Story 2

- [ ] T030 [US2] Implement `SqliteQueryTranslator` translating `ResourceQuery` AST (`Equals`, `Contains`, `Range`) to parameterised SQL with missing-sort-value-last `CASE` ordering in src/persistence/Aster.Persistence.Sqlite/Internal/SqliteQueryTranslator.cs
- [ ] T031 [US2] Implement `SqliteResourceQueryService.QueryAsync` with deterministic tie-break sort, paging, typed `UnsupportedQueryFeature` errors, and slow-query `ILogger` warnings in src/persistence/Aster.Persistence.Sqlite/Persistence/SqliteResourceQueryService.cs

**Checkpoint**: User Stories 1 and 2 both independently functional.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Fulfil the constitution architecture-review obligation, update documentation, and run final end-to-end validation.

- [ ] T032 [P] Author Phase 1 → Phase 2 architecture review document (constitution gate — required before merge) in docs/architecture-review-phase2.md
- [ ] T033 [P] Update `Aster.Core` README and wiki Getting-Started guide documenting `ChannelMode` usage and migration from `bool allowMultipleActive` in src/core/Aster.Core/README.md
- [ ] T034 Execute all quickstart validation scenarios from specs/002-roadmap-next-phase/quickstart.md and capture SC-001..SC-005 evidence
- [ ] T035 Run complete test suite and confirm zero failures across all affected projects before merge

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately; no prerequisites.
- **Phase 2 (Foundational)**: Depends on Phase 1 completion; blocks all user story phases.
- **Phase 3 (US1)**: Depends on Phase 2 completion; delivers the independent MVP increment.
- **Phase 4 (US2)**: Depends on Phase 2 completion; independent of US1 (reuses shared plumbing only).
- **Phase 5 (Polish)**: Depends on all chosen user story phases being complete.

### User Story Dependencies

- **US1 (P1)**: No dependency on other user stories.
- **US2 (P2)**: No dependency on US1; both can proceed in parallel after Phase 2.

### Within Each User Story

- Tests first (expected to initially fail against unimplemented code).
- Store/infrastructure implementation before service wiring.
- Service wiring before host integration updates.
- Story checkpoint must pass before declaring the story complete.

### Parallel Opportunities

- Phase 1: T003, T004 can run in parallel.
- Phase 2: T005–T009 (Aster.Core changes) can proceed before T010–T016 (Sqlite infra); T010+T011 can run in parallel; T015+T016 can run in parallel.
- US1: T017–T021 (tests) can all run in parallel; T022+T023 can run in parallel before T024+T025.
- US2: T026–T029 (tests) can all run in parallel; T030+T031 can run in parallel.
- Phase 5: T032+T033 can run in parallel.

---

## Parallel Example: User Story 1

```bash
Task: "T017 [P] [US1] Add definition registration and round-trip retrieval tests in test/Aster.Tests/Persistence/SqliteDefinitionStoreTests.cs"
Task: "T018 [P] [US1] Add resource append-only write and version retrieval tests in test/Aster.Tests/Persistence/SqliteResourceWriteStoreTests.cs"
Task: "T019 [P] [US1] Add ActivationRecord persistence and ChannelMode durability tests in test/Aster.Tests/Persistence/SqliteActivationTests.cs"
Task: "T020 [P] [US1] Add concurrent save/activate conflict tests in test/Aster.Tests/Persistence/SqliteConcurrencyTests.cs"
Task: "T021 [P] [US1] Add restart durability integration test (SC-001, SC-005) in test/Aster.Tests/Persistence/RestartDurabilityTests.cs"
```

## Parallel Example: User Story 2

```bash
Task: "T026 [P] [US2] Add query filter operator tests in test/Aster.Tests/Persistence/SqliteQueryOperatorTests.cs"
Task: "T027 [P] [US2] Add deterministic paging and sorting tests in test/Aster.Tests/Persistence/SqliteQueryPagingSortingTests.cs"
Task: "T028 [P] [US2] Add missing-sort-value-last tests in test/Aster.Tests/Persistence/SqliteQueryNullSortTests.cs"
Task: "T029 [P] [US2] Add 100k-version query performance integration test in test/Aster.Tests/Persistence/PerformanceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2 (T001–T016).
2. Complete Phase 3 — US1 (T017–T025).
3. Validate US1 independently via restart durability and concurrency tests.
4. Demo or release MVP increment.

### Incremental Delivery

1. Deliver US1 (durable lifecycle — Phase 3).
2. Deliver US2 (persistent querying — Phase 4).
3. Run Phase 5 polish and full quickstart validation.

### Parallel Team Strategy

1. Team aligns on Phase 1 + Phase 2 (Setup + Foundational) tasks.
2. Then split by user story:
   - Engineer A: US1 (Phase 3)
   - Engineer B: US2 (Phase 4)
3. Rejoin for Phase 5 polish and final verification.

---

## Notes

- All tasks use strict checklist format: `- [ ] T### [P?] [US?] Description with file path`.
- `[P]` marks tasks parallelisable with other `[P]` tasks in the same phase.
- `[US#]` labels appear in user story phases only; setup and foundational tasks carry no story label.
- Each user story phase includes independent test criteria, test tasks, and implementation tasks.
- Avoid cross-story coupling beyond shared foundational infrastructure.
