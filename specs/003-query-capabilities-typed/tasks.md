# Tasks: Query Capabilities & Typed Query Helpers

**Input**: Design documents from `/specs/003-query-capabilities-typed/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required because this feature changes public SDK contracts, provider capability declarations, validation behavior, and typed query construction.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm project state and prepare shared query capability surface.

- [X] T001 Confirm Constitution Check gates in `specs/003-query-capabilities-typed/plan.md` still pass before implementation begins
- [X] T002 Create shared query capability model files in `src/core/Aster.Core/Models/Querying/`
- [X] T003 Create shared query capability abstraction files in `src/core/Aster.Core/Abstractions/`
- [X] T004 Create test file placeholders in `test/Aster.Tests/Querying/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data shapes and contracts that all user stories depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Implement `QueryCapabilityDescription` in `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`
- [X] T006 [P] Implement `QueryValidationResult` in `src/core/Aster.Core/Models/Querying/QueryValidationResult.cs`
- [X] T007 [P] Implement `QueryValidationFailure` in `src/core/Aster.Core/Models/Querying/QueryValidationFailure.cs`
- [X] T008 [P] Implement query capability category/value-shape supporting enums or records in `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`
- [X] T009 Implement `IResourceQueryCapabilitiesProvider` in `src/core/Aster.Core/Abstractions/IResourceQueryCapabilitiesProvider.cs`
- [X] T010 Implement `IResourceQueryValidator` in `src/core/Aster.Core/Abstractions/IResourceQueryValidator.cs`
- [X] T011 Register the shared query validator in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T012 Document why the new abstractions are required by current multi-provider validation needs in `specs/003-query-capabilities-typed/plan.md`

**Checkpoint**: Shared capability and validation contracts compile and are ready for provider/story work.

---

## Phase 3: User Story 1 - Discover Provider Query Support (Priority: P1) MVP

**Goal**: Developers can inspect the active provider's supported query scopes, filters, operators, sorts, paging, value shapes, and exclusions.

**Independent Test**: Resolve the active capability provider for in-memory and SQLite JSON configurations and verify each reports its real supported query subset without executing a query.

### Tests for User Story 1

- [X] T013 [P] [US1] Add in-memory capability declaration tests in `test/Aster.Tests/InMemory/InMemoryQueryCapabilityTests.cs`
- [X] T014 [P] [US1] Add SQLite JSON capability declaration tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryCapabilityTests.cs`
- [X] T015 [P] [US1] Add capability discovery DI tests in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`

### Implementation for User Story 1

- [X] T016 [US1] Implement `InMemoryQueryCapabilitiesProvider` in `src/core/Aster.Core/InMemory/InMemoryQueryCapabilitiesProvider.cs`
- [X] T017 [US1] Register `InMemoryQueryCapabilitiesProvider` as `IResourceQueryCapabilitiesProvider` in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T018 [US1] Implement `SqliteJsonQueryCapabilitiesProvider` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryCapabilitiesProvider.cs`
- [X] T019 [US1] Register `SqliteJsonQueryCapabilitiesProvider` as `IResourceQueryCapabilitiesProvider` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [X] T020 [US1] Ensure in-memory capabilities include facet sorting and date-like range behavior matching `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T021 [US1] Ensure SQLite JSON capabilities exclude facet sorting and date-like facet ranges matching `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`

**Checkpoint**: User Story 1 is independently functional and provider support is discoverable without query execution.

---

## Phase 4: User Story 2 - Validate Queries Before Execution (Priority: P1)

**Goal**: Developers can preflight a `ResourceQuery` against the active provider and receive a structured result with all detectable failures.

**Independent Test**: Validate supported and unsupported queries against declared in-memory and SQLite JSON capabilities, including missing capabilities, multiple failures, and unsupported facet sorting.

### Tests for User Story 2

- [X] T022 [P] [US2] Add validator success and non-mutating query tests in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`
- [X] T023 [P] [US2] Add validator multiple-failure aggregation tests in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`
- [X] T024 [P] [US2] Add missing-capabilities fail-closed tests in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`
- [X] T025 [P] [US2] Add SQLite unsupported facet sorting validation tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryCapabilityTests.cs`
- [X] T026 [P] [US2] Add validation/execution consistency tests for unsupported SQLite shapes in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [X] T060 [P] [US2] Add validation-before-provider-change execution enforcement tests in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`

### Implementation for User Story 2

- [X] T027 [US2] Implement `ResourceQueryValidator` in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T028 [US2] Implement validation traversal for `ResourceQuery.Scope`, `ActivationChannel`, `Skip`, and `Take` in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T029 [US2] Implement validation traversal for `MetadataFilter`, `AspectPresenceFilter`, `FacetValueFilter`, and `LogicalExpression` in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T030 [US2] Implement validation traversal for metadata and facet `SortExpression` values in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T031 [US2] Implement value-shape validation for `RangeValue` and numeric facet ranges in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T032 [US2] Implement stable failure codes, messages, paths, and feature categories in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T033 [US2] Ensure `SqliteJsonQueryService` unsupported execution behavior remains explicit in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`

**Checkpoint**: User Story 2 is independently functional and unsupported query shapes can be preflighted before execution.

---

## Phase 5: User Story 3 - Build Queries With Typed Helpers (Priority: P2)

**Goal**: Developers can construct common typed aspect queries without manually typing aspect keys and facet identifiers, while still producing inspectable `ResourceQuery`/`FilterExpression` records.

**Independent Test**: Build typed filters for presence, equality, contains, numeric range, and per-query overrides; inspect the generated query model and validate/execute it with supported providers.

### Tests for User Story 3

- [X] T034 [P] [US3] Add typed helper convention mapping tests in `test/Aster.Tests/Querying/TypedQueryHelperTests.cs`
- [X] T035 [P] [US3] Add typed helper per-query override tests in `test/Aster.Tests/Querying/TypedQueryHelperTests.cs`
- [X] T036 [P] [US3] Add typed helper invalid member selection tests in `test/Aster.Tests/Querying/TypedQueryHelperTests.cs`
- [X] T037 [P] [US3] Add typed helper validation integration tests in `test/Aster.Tests/Querying/TypedQueryHelperTests.cs`
- [X] T061 [P] [US3] Add typed helper generated-query execution tests against a supported provider in `test/Aster.Tests/Querying/TypedQueryHelperTests.cs`

### Implementation for User Story 3

- [X] T038 [US3] Implement `TypedQueryOptions` in `src/core/Aster.Core/Models/Querying/TypedQueryOptions.cs`
- [X] T039 [US3] Implement `TypedQuery` entry points in `src/core/Aster.Core/Extensions/TypedQuery.cs`
- [X] T040 [US3] Implement typed aspect presence helper outputting `AspectPresenceFilter` in `src/core/Aster.Core/Extensions/TypedQuery.cs`
- [X] T041 [US3] Implement typed facet member selection with CLR type-name/member-name convention in `src/core/Aster.Core/Extensions/TypedQuery.cs`
- [X] T042 [US3] Implement typed equality, contains, and numeric range helper outputting `FacetValueFilter` in `src/core/Aster.Core/Extensions/TypedQuery.cs`
- [X] T043 [US3] Implement per-query aspect key and facet identifier override support in `src/core/Aster.Core/Extensions/TypedQuery.cs`
- [X] T044 [US3] Implement clear failure behavior for unsupported or non-member selector expressions in `src/core/Aster.Core/Extensions/TypedQuery.cs`

**Checkpoint**: User Story 3 is independently functional and typed helpers produce inspectable portable query records.

---

## Phase 6: User Story 4 - Preserve Provider-Agnostic Query Semantics (Priority: P3)

**Goal**: The new helper and capability APIs reinforce the portable query model and do not introduce a public provider-specific query contract.

**Independent Test**: Confirm helper output is `ResourceQuery`/`FilterExpression`, no public `IQueryable<Resource>` provider surface exists, and provider-specific differences are expressed through capabilities and validation.

### Tests for User Story 4

- [X] T045 [P] [US4] Add no public `IQueryable<Resource>` API regression tests in `test/Aster.Tests/Querying/TypedQueryHelperTests.cs`
- [X] T046 [P] [US4] Add provider difference tests comparing in-memory and SQLite capabilities in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T047 [P] [US4] Add manual query compatibility tests in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`

### Implementation for User Story 4

- [X] T048 [US4] Review public query APIs for absence of `IQueryable<Resource>` exposure in `src/core/Aster.Core/Abstractions/`
- [X] T049 [US4] Ensure manually built `ResourceQuery` usage remains unchanged in `src/core/Aster.Core/Models/Querying/ResourceQuery.cs`
- [X] T050 [US4] Ensure provider-specific differences are represented only through capabilities and validation in `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`

**Checkpoint**: User Story 4 is independently functional and the provider-agnostic query architecture is preserved.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final verification across all stories.

- [X] T051 [P] Update querying documentation with capabilities, validation, and typed helper examples in `wiki/Querying.md`
- [X] T052 [P] Update DI documentation with capability provider and validator registrations in `wiki/DI-Registration.md`
- [X] T053 [P] Update root README query examples with typed helper and validation snippets in `README.md`
- [X] T054 [P] Update `src/core/Aster.Core/README.md` with typed helper and validation quick examples
- [X] T055 [P] Update SQLite provider README with declared capability subset in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T056 Run quickstart validation from `specs/003-query-capabilities-typed/quickstart.md`
- [X] T057 Re-run Constitution Check and remove unnecessary abstractions/dependencies before final verification in `specs/003-query-capabilities-typed/plan.md`
- [X] T058 Run `dotnet test Aster.sln`
- [X] T059 Review capability declarations against provider execution behavior in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs` and `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **US1 Discover Provider Query Support (Phase 3)**: Depends on Foundational and is the MVP.
- **US2 Validate Queries Before Execution (Phase 4)**: Depends on Foundational and capability declarations from US1.
- **US3 Build Queries With Typed Helpers (Phase 5)**: Depends on Foundational; validation integration depends on US2.
- **US4 Preserve Provider-Agnostic Query Semantics (Phase 6)**: Depends on US1-US3 surfaces being present.
- **Polish (Phase 7)**: Depends on desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational; no dependencies on other stories.
- **User Story 2 (P1)**: Requires capability declarations from US1 to validate real providers.
- **User Story 3 (P2)**: Can start after Foundational for helper output; validation integration requires US2.
- **User Story 4 (P3)**: Requires public surfaces from US1-US3 to verify architecture preservation.

### Parallel Opportunities

- T006-T008 can run in parallel after T005 starts.
- T013-T015 can run in parallel.
- T016 and T018 can run in parallel after foundational contracts exist.
- T022-T026 and T060 can run in parallel.
- T034-T037 and T061 can run in parallel.
- T045-T047 can run in parallel.
- T051-T055 can run in parallel after implementation stabilizes.

## Parallel Example: User Story 1

```text
Task: "Add in-memory capability declaration tests in test/Aster.Tests/InMemory/InMemoryQueryCapabilityTests.cs"
Task: "Add SQLite JSON capability declaration tests in test/Aster.Tests/SqliteJson/SqliteJsonQueryCapabilityTests.cs"
Task: "Add capability discovery DI tests in test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "Add typed helper convention mapping tests in test/Aster.Tests/Querying/TypedQueryHelperTests.cs"
Task: "Add typed helper per-query override tests in test/Aster.Tests/Querying/TypedQueryHelperTests.cs"
Task: "Add typed helper invalid member selection tests in test/Aster.Tests/Querying/TypedQueryHelperTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Setup and Foundational phases.
2. Complete US1 capability declarations and DI registration.
3. Run provider capability tests.
4. Validate that developers can inspect in-memory and SQLite JSON support before executing a query.

### Incremental Delivery

1. US1: Provider capabilities are discoverable.
2. US2: Queries can be validated against capabilities.
3. US3: Typed helpers produce inspectable portable query records.
4. US4: Architecture guardrails and compatibility are verified.
5. Polish: Documentation and quickstart validation.

### Quality Gates

- Each story must pass its story-specific tests before moving to the next dependent story.
- `dotnet test Aster.sln` must pass before implementation is considered complete.
- No task should introduce a public `IQueryable<Resource>` execution surface.
