# Tasks: SQLite JSON Querying (Phase 2A)

**Input**: Design documents from `/specs/002-sqlite-json-querying/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Tests are required for each user story because this feature changes provider query execution semantics.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare provider query service files and registration points.

- [x] T001 Confirm Constitution Check gates in `specs/002-sqlite-json-querying/plan.md` still pass before implementation begins
- [x] T002 Create `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T003 [P] Create `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteParameterBag.cs`
- [x] T004 [P] Create `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteQueryBuilder.cs`
- [x] T005 [P] Create `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T006 [P] Create `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteJsonPath.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core translation/execution structure required before user stories.

**CRITICAL**: No user story work can be completed until this phase is complete.

- [x] T007 Register `SqliteJsonQueryService` as `IResourceQueryService` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [x] T008 Implement shared SQLite command execution and payload deserialization in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T009 Implement query input validation for `Scope`, `ActivationChannel`, `Skip`, `Take`, and empty `RangeValue` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T010 Implement unsupported-feature helper methods that throw `UnsupportedQueryFeatureException` with actionable messages in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T011 Document and justify that no new abstraction or third-party dependency is introduced for this feature

**Checkpoint**: SQLite query service can be resolved but does not yet need to support all query stories.

---

## Phase 3: User Story 1 - Query Persisted Resources By Metadata (Priority: P1)

**Goal**: Execute metadata, scope, sort, and paging queries in SQLite.

**Independent Test**: Persist resources, recreate the service provider, query through `IResourceQueryService`, and verify metadata filters, scopes, sorting, and paging.

### Tests for User Story 1

- [x] T012 [P] [US1] Add SQLite metadata filtering tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T013 [P] [US1] Add latest/all/active/draft scope tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T014 [P] [US1] Add metadata sort, `Skip`, and `Take` tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`

### Implementation for User Story 1

- [x] T015 [US1] Implement base scope SQL for `Latest` and `AllVersions` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T016 [US1] Implement `Active` and `Draft` scope SQL using `activation_states` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T017 [US1] Implement `DefinitionId` shortcut filtering in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T018 [US1] Implement `MetadataFilter` translation for supported metadata fields in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T019 [US1] Implement `LogicalExpression` translation for `And`, `Or`, and `Not` in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T020 [US1] Implement metadata `SortExpression` translation in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T021 [US1] Implement SQL `LIMIT`/`OFFSET` for `Take`/`Skip` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`

**Checkpoint**: User Story 1 works independently and all US1 tests pass.

---

## Phase 4: User Story 2 - Filter Persisted Facet Values In SQLite JSON (Priority: P2)

**Goal**: Execute simple scalar JSON aspect/facet filters in SQLite.

**Independent Test**: Persist typed aspect payloads, recreate the provider, and verify facet `Equals`, `Contains`, and `Range` queries.

### Tests for User Story 2

- [x] T022 [P] [US2] Add `AspectPresenceFilter` tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T023 [P] [US2] Add `FacetValueFilter` `Equals` tests for strings/numbers in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T024 [P] [US2] Add `FacetValueFilter` `Contains` tests for string facets in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T025 [P] [US2] Add `FacetValueFilter` `Range` tests for numeric facets in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T026 [P] [US2] Add missing aspect/facet non-match tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`

### Implementation for User Story 2

- [x] T027 [US2] Implement safe JSON path segment encoding in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteJsonPath.cs`
- [x] T028 [US2] Implement `AspectPresenceFilter` translation using SQLite JSON functions in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T029 [US2] Implement facet `Equals` translation in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T030 [US2] Implement facet `Contains` translation for strings in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T031 [US2] Implement facet `Range` translation for numeric scalar values in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`

**Checkpoint**: User Story 2 works independently and all US2 tests pass.

---

## Phase 5: User Story 3 - Reject Unsupported Query Shapes Explicitly (Priority: P3)

**Goal**: Fail unsupported provider query shapes with clear typed errors.

**Independent Test**: Execute unsupported query shapes and verify `UnsupportedQueryFeatureException`.

### Tests for User Story 3

- [x] T032 [P] [US3] Add unsupported metadata field tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T033 [P] [US3] Add unsupported facet sort tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T034 [P] [US3] Add invalid active scope and invalid paging tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T035 [P] [US3] Add no in-memory fallback regression test in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [x] T036 [P] [US3] Add no `IQueryable<Resource>` / no LINQ-provider API regression check in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`

### Implementation for User Story 3

- [x] T037 [US3] Ensure unsupported sorts fail before SQL execution in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [x] T038 [US3] Ensure unsupported predicates fail before SQL execution in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [x] T039 [US3] Ensure invalid query inputs fail before SQL execution in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`

**Checkpoint**: User Story 3 works independently and unsupported behavior is explicit.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final verification.

- [x] T040 [P] Update `src/persistence/Aster.Persistence.SqliteJson/README.md` with SQLite query support and unsupported behavior
- [x] T041 [P] Update `wiki/Querying.md` with SQLite provider query subset and future typed-helper direction
- [x] T042 Re-run Constitution Check and remove unnecessary abstractions/dependencies before final verification
- [x] T043 Run `dotnet test Aster.sln`
- [x] T044 Review generated SQL helpers for readability, explicitness, parameter usage, and safe JSON path construction

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **US1 (Phase 3)**: Depends on Foundational.
- **US2 (Phase 4)**: Depends on Foundational; can start after US1 structure exists, but tests should remain independently understandable.
- **US3 (Phase 5)**: Depends on Foundational; can run alongside US1/US2 once translator shape exists.
- **Polish (Phase 6)**: Depends on desired user stories being complete.

### Parallel Opportunities

- T003-T006 can run in parallel.
- T012-T014 can run in parallel.
- T022-T026 can run in parallel.
- T032-T036 can run in parallel.
- T040-T041 can run in parallel.

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Setup and Foundational tasks.
2. Complete US1 tests and implementation.
3. Run `dotnet test Aster.sln`.
4. Validate provider-backed metadata/scope queries independently before starting facet JSON filters.

### Full Feature

1. Complete MVP.
2. Add US2 facet JSON filtering.
3. Add US3 unsupported-query behavior.
4. Complete docs and final verification.
