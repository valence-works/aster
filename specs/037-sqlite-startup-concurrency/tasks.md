# Tasks: SQLite Startup Concurrency Hardening

**Input**: Design documents from `/specs/037-sqlite-startup-concurrency/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sqlite-startup-concurrency.md, quickstart.md

**Tests**: Required. This is a test-focused operational hardening slice.

**Organization**: Tasks are grouped by independently testable startup scenario.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm branch, scope, and existing test patterns before implementation.

- [X] T001 Confirm branch `037-sqlite-startup-concurrency` is active
- [X] T002 Review existing SQLite idempotency test helpers in `test/Aster.Tests/SqliteJson/SqliteJsonSchemaIdempotencyTests.cs`
- [X] T003 Review SQLite schema initialization behavior in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`
- [X] T004 Confirm Constitution Check gates still pass before implementation begins

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared test helpers for bounded concurrent startup attempts.

**CRITICAL**: No user story work should begin until this phase is complete.

- [X] T005 Add or reuse temporary database and provider construction helpers in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`
- [X] T006 Add or reuse seeded definition/resource/activation/marker fixture helpers in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`
- [X] T007 Add or reuse table metadata/count assertion helpers in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`

**Checkpoint**: Shared concurrent startup test setup is ready.

---

## Phase 3: User Story 1 - Concurrent Fresh Startup Is Safe (Priority: P1)

**Goal**: Prove several simultaneous SQLite JSON startup paths can initialize a fresh database and leave it usable.

**Independent Test**: Run only the fresh-startup test and verify initialization plus subsequent read/write behavior.

### Tests for User Story 1

- [X] T008 [US1] Add concurrent fresh-database startup test in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`
- [X] T009 [US1] Verify post-startup definition/resource save and read behavior in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`

### Implementation for User Story 1

- [X] T010 [US1] Confirm no production SQLite schema/provider fix is needed for fresh concurrent startup in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`

**Checkpoint**: Fresh database concurrent startup is independently covered.

---

## Phase 4: User Story 2 - Concurrent Existing Startup Preserves Data (Priority: P2)

**Goal**: Prove several simultaneous SQLite JSON startup paths preserve seeded tenant-aware data and table shape.

**Independent Test**: Run only the existing-startup test and verify definitions, resources, activation state, lifecycle markers, table shape, row counts, and absence of bootstrap leftovers.

### Tests for User Story 2

- [X] T011 [US2] Add seeded existing-database concurrent startup test in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`
- [X] T012 [US2] Assert seeded definitions/resources/activation/markers remain intact in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`
- [X] T013 [US2] Assert tenant-aware table metadata, row counts, and no bootstrap leftovers in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`

### Implementation for User Story 2

- [X] T014 [US2] Confirm no production SQLite schema/provider fix is needed for existing concurrent startup in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`

**Checkpoint**: Existing tenant-aware database concurrent startup is independently covered.

---

## Phase 5: User Story 3 - No-Schema Mode Remains Passive (Priority: P3)

**Goal**: Prove concurrent no-schema service construction remains passive.

**Independent Test**: Run only the no-schema concurrency test and verify no SQLite file is created.

### Tests for User Story 3

- [X] T015 [US3] Add concurrent `InitializeSchema=false` construction test in `test/Aster.Tests/SqliteJson/SqliteJsonStartupConcurrencyTests.cs`

### Implementation for User Story 3

- [X] T016 [US3] Confirm no production SQLite provider fix is needed for concurrent no-schema construction in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`

**Checkpoint**: Passive no-schema concurrent construction is independently covered.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Update tracking artifacts and validate the slice.

- [X] T017 Update `docs/ExecutionRoadmap.md` to mark 036 landed and 037 in progress
- [X] T018 Update `AGENTS.md` Recent Changes entry if implementation details differ from planning assumptions
- [X] T019 Run focused validation from `specs/037-sqlite-startup-concurrency/quickstart.md`
- [X] T020 Run `dotnet test Aster.sln`
- [X] T021 Run `dotnet build Aster.sln /m:1`
- [X] T022 Re-run Constitution Check and remove unnecessary abstractions/dependencies before final verification

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup completion and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on foundational helpers.
- **User Story 2 (Phase 4)**: Depends on foundational helpers and seeded fixture setup.
- **User Story 3 (Phase 5)**: Depends on foundational helpers.
- **Polish (Phase 6)**: Depends on desired user stories.

### User Story Dependencies

- **US1**: No dependency on other user stories after foundational setup.
- **US2**: No dependency on US1, but may reuse fixture helpers.
- **US3**: No dependency on US1/US2 after foundational setup.

### Parallel Opportunities

- US1, US2, and US3 test implementation can proceed independently after foundational helpers.
- T010, T014, and T016 are conditional and should be skipped unless their paired tests expose defects.

## Implementation Strategy

### MVP First

1. Complete setup and foundational helpers.
2. Implement US1 fresh-startup coverage.
3. Run the focused `SqliteJsonStartupConcurrencyTests` filter.

### Incremental Delivery

1. Add US2 existing-database preservation coverage.
2. Add US3 no-schema passive coverage.
3. Update roadmap/context artifacts.
4. Run focused adjacent SQLite tests.
5. Run full test and build gates.
