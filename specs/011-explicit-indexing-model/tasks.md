# Tasks: Explicit Indexing Model

**Input**: Design documents from `/specs/011-explicit-indexing-model/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by FR-001 through FR-019 and SC-001 through SC-004. Write focused tests before implementation changes where practical.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the active feature context and existing query capability surface before changing SDK contracts.

- [X] T001 Confirm `.specify/feature.json` points to `specs/011-explicit-indexing-model`
- [X] T002 Review existing query capability constructor usages in `src/core/Aster.Core`, `src/persistence/Aster.Persistence.SqliteJson`, and `test/Aster.Tests`
- [X] T003 Review current provider capability docs in `wiki/Querying.md`, `wiki/DI-Registration.md`, and `src/core/Aster.Core/README.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared SDK model required by all user stories.

**CRITICAL**: No user story implementation can compile cleanly until this phase is complete.

- [X] T004 [P] Add `IndexFieldType` enum and index projection source/value/result records in `src/core/Aster.Core/Models/Querying/IndexProjections.cs`
- [X] T005 [P] Add projection declaration validation result types in `src/core/Aster.Core/Models/Querying/IndexProjectionValidation.cs`
- [X] T006 Add `IndexProjections` to `QueryCapabilityDescription` in `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`
- [X] T007 Preserve source-compatible capability construction where practical, such as defaulting index projections to an empty collection or providing a compatible construction path, in `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`
- [X] T008 Update all existing `QueryCapabilityDescription` call sites with empty index projections where explicit arguments are still needed in `src/core/Aster.Core`, `src/persistence/Aster.Persistence.SqliteJson`, and `test/Aster.Tests`
- [X] T009 Run `dotnet build Aster.sln /m:1` and fix compile errors caused by the capability contract extension

**Checkpoint**: Core index projection model compiles and existing providers can declare zero projections.

---

## Phase 3: User Story 1 - Declare Queryable Index Projections (Priority: P1) MVP

**Goal**: Provider authors can declare valid metadata and facet index projections through provider capabilities.

**Independent Test**: Declare metadata and facet projections with all supported field types in a custom/test capability provider and verify the declarations are accepted and inspectable.

### Tests for User Story 1

- [X] T010 [P] [US1] Add tests for valid metadata and facet index projection declarations in `test/Aster.Tests/Querying/IndexProjectionDeclarationTests.cs`
- [X] T011 [P] [US1] Add tests for invalid projection declarations including duplicate field names, empty names, invalid metadata source, invalid facet source, and nested/provider-specific source attempts in `test/Aster.Tests/Querying/IndexProjectionDeclarationTests.cs`

### Implementation for User Story 1

- [X] T012 [US1] Implement factory methods for metadata and facet projections in `src/core/Aster.Core/Models/Querying/IndexProjections.cs`
- [X] T013 [US1] Implement projection declaration validation for unique field names, valid sources, and multi-value rules in `src/core/Aster.Core/Services/IndexProjectionValidator.cs`
- [X] T014 [US1] Ensure `KeywordArray` declarations are treated as multi-value projections in `src/core/Aster.Core/Models/Querying/IndexProjections.cs`
- [X] T015 [US1] Run focused declaration tests with `dotnet test Aster.sln --filter IndexProjectionDeclarationTests`

**Checkpoint**: User Story 1 is independently functional; custom providers can declare inspectable projection metadata.

---

## Phase 4: User Story 2 - Keep Provider Capability Discovery Honest (Priority: P2)

**Goal**: Capability discovery clearly exposes zero built-in projections and explicit custom/test provider projections without hidden discovery.

**Independent Test**: Inspect built-in and custom provider capabilities and verify built-ins declare zero projections while a custom provider can declare one or more.

### Tests for User Story 2

- [X] T016 [P] [US2] Add built-in provider zero-projection assertions in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T017 [P] [US2] Add custom provider projection discovery assertions in `test/Aster.Tests/Querying/ProviderAuthoringTests.cs`
- [X] T018 [P] [US2] Add provider conformance coverage that accepts empty index declarations and validates duplicate declaration diagnostics in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`

### Implementation for User Story 2

- [X] T019 [US2] Wire index projection declaration validation into provider conformance evaluation in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`
- [X] T020 [US2] Run focused capability tests with `dotnet test Aster.sln --filter \"QueryCapabilityDiscoveryTests|ProviderAuthoringTests|ProviderConformanceTests\"`

**Checkpoint**: User Story 2 is independently functional; capability discovery is explicit and built-ins do not imply physical indexes.

---

## Phase 5: User Story 3 - Consume Projection Values Consistently (Priority: P3)

**Goal**: Provider implementers can evaluate declared projections against a resource version and receive typed values plus structured failures.

**Independent Test**: Apply metadata and facet projections to representative resource versions and verify valid values, missing-source failures, incompatible-shape failures, strict shape matching, and DateTime normalization.

### Tests for User Story 3

- [X] T021 [P] [US3] Add projection evaluation success tests for metadata and facet projections in `test/Aster.Tests/Querying/IndexProjectionEvaluationTests.cs`
- [X] T022 [P] [US3] Add projection evaluation failure tests for missing source, incompatible shape, scalar receiving array, `KeywordArray` receiving scalar, and one failure with other successful values in `test/Aster.Tests/Querying/IndexProjectionEvaluationTests.cs`
- [X] T023 [P] [US3] Add strict value-shape tests proving numeric, boolean, and GUID-looking strings are not coerced in `test/Aster.Tests/Querying/IndexProjectionEvaluationTests.cs`
- [X] T024 [P] [US3] Add DateTime projection tests for accepted DateTime values and rejected date-only strings in `test/Aster.Tests/Querying/IndexProjectionEvaluationTests.cs`

### Implementation for User Story 3

- [X] T025 [US3] Implement projection evaluator for metadata and aspect/facet sources in `src/core/Aster.Core/Services/IndexProjectionEvaluator.cs`
- [X] T026 [US3] Implement strict value-shape matching for all `IndexFieldType` values in `src/core/Aster.Core/Services/IndexProjectionEvaluator.cs`
- [X] T027 [US3] Reuse `QueryDateTimeValue` for DateTime projection normalization in `src/core/Aster.Core/Services/IndexProjectionEvaluator.cs`
- [X] T028 [US3] Implement structured projection failure codes `missing-source`, `incompatible-value-shape`, and `invalid-projection-declaration` in `src/core/Aster.Core/Models/Querying/IndexProjectionValidation.cs`
- [X] T029 [US3] Run focused evaluation tests with `dotnet test Aster.sln --filter IndexProjectionEvaluationTests`

**Checkpoint**: User Story 3 is independently functional; projection evaluation is fail-soft and strict.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, compatibility verification, and final validation.

- [X] T030 [P] Update provider capability and indexing guidance in `wiki/Querying.md`
- [X] T031 [P] Update DI/provider authoring docs with index projection declaration guidance in `wiki/DI-Registration.md`
- [X] T032 [P] Update core README capability overview in `src/core/Aster.Core/README.md`
- [X] T033 [P] Update quickstart examples if final public API names differ in `specs/011-explicit-indexing-model/quickstart.md`
- [X] T034 Re-run Constitution Check against implemented design and remove any unnecessary abstraction in `specs/011-explicit-indexing-model/plan.md`
- [X] T035 Run `dotnet test Aster.sln`
- [X] T036 Run `dotnet build Aster.sln /m:1`
- [X] T037 Run `git diff --check`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories because it extends the core capability contract.
- **User Story 1 (Phase 3)**: Depends on Foundational.
- **User Story 2 (Phase 4)**: Depends on US1 because provider conformance validation uses the projection declaration validation helper.
- **User Story 3 (Phase 5)**: Depends on US1 because evaluation tests and implementation use the projection model factories.
- **Polish (Phase 6)**: Depends on completed target user stories.

### User Story Dependencies

- **US1**: MVP. No dependency on US2 or US3 after Foundation.
- **US2**: Depends on US1 declaration factories and validation helper.
- **US3**: Depends on US1 projection factories and declaration model.

### Within Each User Story

- Write story tests first and confirm they fail where practical.
- Implement public model/factory APIs before services that consume them.
- Run focused tests at the story checkpoint.
- Do not add physical indexes, migrations, query planning, runtime scanning, public SQL, or `IQueryable<Resource>`.

---

## Parallel Opportunities

- T004 and T005 can run in parallel after Setup because they create separate model files.
- T010 and T011 can run in parallel because they add independent declaration test cases in the same new test file only if coordinated; otherwise run sequentially to avoid edit conflicts.
- T016, T017, and T018 can run in parallel because they target different test areas.
- T021, T022, T023, and T024 can run in parallel only if assigned to separate sections of the new evaluation test file; otherwise run sequentially to avoid edit conflicts.
- T030, T031, T032, and T033 can run in parallel because they update different docs.

## Parallel Example: User Story 2

```text
Task: "Add built-in provider zero-projection assertions in test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs"
Task: "Add custom provider projection discovery assertions in test/Aster.Tests/Querying/ProviderAuthoringTests.cs"
Task: "Add provider conformance coverage in test/Aster.Tests/Querying/ProviderConformanceTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Add declaration tests for supported and invalid projections.
3. Implement projection model factories and declaration validation.
4. Run focused declaration tests.

### Incremental Delivery

1. US1 establishes the public declaration model.
2. US2 proves capability discovery and provider conformance remain honest.
3. US3 adds projection evaluation for provider authors.
4. Polish updates docs and runs full validation.

### Guardrails

- Keep built-in providers at zero default projections.
- Keep source shapes limited to metadata fields and aspect/facet pairs.
- Keep evaluation strict except for existing DateTime normalization.
- Preserve existing query validation and execution behavior.
