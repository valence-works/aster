# Tasks: Provider Validation Execution Alignment

**Input**: Design documents from `/specs/004-provider-validation-execution/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/provider-validation-execution.md`, `quickstart.md`

**Tests**: Tests are required for this feature because the specification requires provider consistency coverage and explicit validation/execution mismatch detection.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently after the shared foundation is in place.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and does not depend on another incomplete task.
- **[Story]**: Maps the task to the user story in `spec.md`.
- Each task includes an exact repository path.

## Phase 1: Setup

**Purpose**: Confirm the existing query surface and prepare story-specific test locations before changing shared contracts.

- [X] T001 Review the current query capability and validation contracts in `src/core/Aster.Core/Abstractions/IResourceQueryCapabilitiesProvider.cs`, `src/core/Aster.Core/Abstractions/IResourceQueryService.cs`, and `src/core/Aster.Core/Abstractions/IResourceQueryValidator.cs`
- [X] T002 Review the current unsupported query exception and provider execution guards in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`, `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`, `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`, `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteQueryBuilder.cs`, and `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [X] T003 [P] Review existing provider capability tests in `test/Aster.Tests/InMemory/InMemoryQueryCapabilityTests.cs`, `test/Aster.Tests/SqliteJson/SqliteJsonQueryCapabilityTests.cs`, and `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T004 [P] Review existing provider execution tests in `test/Aster.Tests/InMemory/InMemoryQueryServiceTests.cs` and `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [X] T005 Confirm the Constitution Check entries in `specs/004-provider-validation-execution/plan.md` still match the intended implementation scope

---

## Phase 2: Foundational

**Purpose**: Add shared provider identity and structured unsupported-failure primitives that all user stories depend on.

**Critical**: No user story implementation should begin until these shared contracts compile.

- [X] T006 Add the explicit provider identity contract in `src/core/Aster.Core/Abstractions/IResourceQueryProviderIdentity.cs`
- [X] T007 Add `ProviderKey` to `QueryCapabilityDescription` and its XML documentation in `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`
- [X] T008 Update all `QueryCapabilityDescription` construction call sites to pass provider keys in `src/core/Aster.Core/InMemory/InMemoryQueryCapabilitiesProvider.cs` and `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryCapabilitiesProvider.cs`
- [X] T009 Add stable `Code`, `Feature`, and nullable `Path` properties plus modern constructors to `UnsupportedQueryFeatureException` in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`
- [X] T010 Add an exception factory or constructor that maps `QueryValidationFailure` to `UnsupportedQueryFeatureException` in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`
- [X] T011 Make `InMemoryQueryService` expose the in-memory provider key through `IResourceQueryProviderIdentity` in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T012 Make `SqliteJsonQueryService` expose the SQLite JSON provider key through `IResourceQueryProviderIdentity` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [X] T013 Add matching provider key constants or shared provider-key values for capability providers and query services in `src/core/Aster.Core/InMemory/InMemoryQueryCapabilitiesProvider.cs` and `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryCapabilitiesProvider.cs`
- [X] T014 Replace type-name capability matching with explicit provider-key matching in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T015 Preserve only the direct-construction validation fallback for tests and non-DI callers while keeping DI-based provider matching fail-closed in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T016 Build the solution to catch public contract call-site breaks with `dotnet build Aster.sln`

**Checkpoint**: Provider identity, capability declarations, structured exceptions, and validator matching compile.

---

## Phase 3: User Story 1 - Execute With Consistent Unsupported-Query Feedback (Priority: P1)

**Goal**: Provider execution failures expose stable code/category/message data that aligns with preflight validation for unsupported query shapes.

**Independent Test**: Execute unsupported queries directly against providers without caller preflight and confirm the thrown failure code, feature category, path, and message align with validation failures.

### Tests for User Story 1

- [X] T017 [P] [US1] Add SQLite execution failure assertions for unsupported facet sorting in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [X] T018 [P] [US1] Add SQLite validation/execution consistency tests for unsupported metadata contains field, metadata range filter, facet sort, and date-like range shapes in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [X] T019 [P] [US1] Add in-memory execution failure assertions for unknown filter, unknown logical operator, and unknown comparator shapes in `test/Aster.Tests/InMemory/InMemoryQueryServiceTests.cs`
- [X] T020 [P] [US1] Add supported-query validation/execution consistency cases for in-memory and SQLite providers in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`

### Implementation for User Story 1

- [X] T021 [US1] Inject or otherwise compose shared query validation into `SqliteJsonQueryService` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [X] T022 [US1] Run shared validation before SQLite translation and throw the first blocking validation failure as `UnsupportedQueryFeatureException` in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [X] T023 [US1] Replace SQLite service-level unsupported scope, activation, skip, and take exceptions with structured unsupported failures in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [X] T024 [US1] Replace SQLite sort builder unsupported failures with structured code/feature/path values in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteQueryBuilder.cs`
- [X] T025 [US1] Replace SQLite predicate translator unsupported failures with structured code/feature/path values in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [X] T026 [US1] Inject or otherwise compose shared query validation into `InMemoryQueryService` and run it before in-memory evaluation in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T027 [US1] Replace in-memory evaluator unsupported failures with structured code/feature/path values in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T028 [US1] Ensure execution failure messages remain actionable and provider-specific without relying on message text for tests in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`
- [X] T029 [US1] Run US1 tests with `dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonQueryServiceTests|FullyQualifiedName~InMemoryQueryServiceTests|FullyQualifiedName~QueryCapabilityDiscoveryTests"`

**Checkpoint**: Unsupported execution failures are structured and align with validation categories for the covered query shapes.

---

## Phase 4: User Story 2 - Fail Closed For Providers Without Declared Capabilities (Priority: P1)

**Goal**: Validation uses the active provider's explicit provider key and fails closed when no matching capability declaration exists.

**Independent Test**: Register a custom active query provider without matching capabilities and confirm validation returns a `capabilities-not-declared` failure.

### Tests for User Story 2

- [X] T030 [P] [US2] Add a custom active provider without matching capabilities test in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`
- [X] T031 [P] [US2] Add a provider-key mismatch fails-closed test in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`
- [X] T032 [P] [US2] Add a registration-order test proving explicit provider-key matching selects the active provider capabilities in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T033 [P] [US2] Add provider key assertions for in-memory and SQLite capability declarations in `test/Aster.Tests/InMemory/InMemoryQueryCapabilityTests.cs` and `test/Aster.Tests/SqliteJson/SqliteJsonQueryCapabilityTests.cs`

### Implementation for User Story 2

- [X] T034 [US2] Validate non-empty provider keys on capability declarations in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T035 [US2] Return a `capabilities-not-declared` validation failure when the active provider key has no matching declaration in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T036 [US2] Ensure validation chooses exact provider-key matches deterministically when multiple capability providers are registered in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T037 [US2] Update service registration code if needed so active query services and capability providers are available to validation in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs` and `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [X] T038 [US2] Run US2 tests with `dotnet test Aster.sln --filter "FullyQualifiedName~ResourceQueryValidatorTests|FullyQualifiedName~QueryCapabilityDiscoveryTests|FullyQualifiedName~QueryCapabilityTests"`

**Checkpoint**: Validation no longer validates custom or replaced providers against stale default capabilities.

---

## Phase 5: User Story 3 - Keep Provider Execution Authoritative (Priority: P2)

**Goal**: Providers reuse shared validation but keep provider-specific execution and translation safeguards.

**Independent Test**: Skip caller preflight, execute invalid or unsupported query shapes, and confirm providers still reject them explicitly while supported queries still execute.

### Tests for User Story 3

- [X] T039 [P] [US3] Add SQLite tests proving provider-specific translator constraints still reject invalid range and logical expression shapes in `test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs`
- [X] T040 [P] [US3] Add in-memory tests proving execution still rejects unsupported expression enum values when caller preflight is skipped in `test/Aster.Tests/InMemory/InMemoryQueryServiceTests.cs`
- [X] T041 [P] [US3] Add provider consistency helper coverage for validation-accepts/execution-rejects drift in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T042 [P] [US3] Add provider consistency helper coverage for validation-rejects/execution-supports drift in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T043 [P] [US3] Add a multi-failure validation test proving validation still reports all detectable unsupported features in `test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs`

### Implementation for User Story 3

- [X] T044 [US3] Keep SQLite translator guard clauses after shared validation in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [X] T045 [US3] Keep SQLite sort and scope guard clauses after shared validation in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteQueryBuilder.cs`
- [X] T046 [US3] Keep in-memory evaluator guard clauses for unknown filters, operators, and comparators in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T047 [US3] Add or refine provider consistency test helpers for code/category comparison in `test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs`
- [X] T048 [US3] Run US3 tests with `dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonQueryServiceTests|FullyQualifiedName~InMemoryQueryServiceTests|FullyQualifiedName~QueryCapabilityDiscoveryTests|FullyQualifiedName~ResourceQueryValidatorTests"`

**Checkpoint**: Shared validation reduces drift, but provider execution remains the authoritative boundary.

---

## Phase 6: User Story 4 - Document The Recommended Query Flow (Priority: P3)

**Goal**: SDK consumers can discover capabilities, validate queries, execute supported queries, and handle authoritative execution failures.

**Independent Test**: Follow the documentation examples to inspect capabilities, validate a query, handle validation failures, and catch structured unsupported execution failures.

### Tests for User Story 4

- [X] T049 [P] [US4] Verify documentation snippets compile conceptually against current public APIs in `specs/004-provider-validation-execution/quickstart.md`

### Implementation for User Story 4

- [X] T050 [US4] Document provider capability discovery, validation, execution, and failure handling in `wiki/Querying.md`
- [X] T051 [US4] Document explicit provider keys and fail-closed capability matching in `wiki/DI-Registration.md`
- [X] T052 [US4] Document structured unsupported query exceptions in `wiki/Exception-Reference.md`
- [X] T053 [US4] Update core SDK query guidance with provider validation flow in `src/core/Aster.Core/README.md`
- [X] T054 [US4] Update SQLite provider query capability guidance in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T055 [US4] Re-run the quickstart scenario manually against the public API descriptions in `specs/004-provider-validation-execution/quickstart.md`

**Checkpoint**: Documentation explains validation as advisory, execution as authoritative, and custom provider capability matching as explicit.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verify the full feature, keep the implementation small, and prepare for review.

- [X] T056 [P] Review public API XML documentation for provider identity, capability provider keys, validation failures, and unsupported execution failures in `src/core/Aster.Core/Abstractions/IResourceQueryProviderIdentity.cs`, `src/core/Aster.Core/Models/Querying/QueryCapabilityDescription.cs`, and `src/core/Aster.Core/Exceptions/AsterExceptions.cs`
- [X] T057 [P] Review docs for consistency of provider key names, failure codes, and unsupported feature categories in `wiki/Querying.md`, `wiki/DI-Registration.md`, `wiki/Exception-Reference.md`, `src/core/Aster.Core/README.md`, and `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T058 Confirm no public raw SQL, public `IQueryable<Resource>`, or provider-specific query construction contract was introduced in `src/core/Aster.Core/Abstractions/IResourceQueryService.cs` and `src/core/Aster.Core/Extensions/TypedQuery.cs`
- [X] T059 Re-run the Constitution Check against implementation decisions in `specs/004-provider-validation-execution/plan.md`
- [X] T060 Run the full test suite with `dotnet test Aster.sln`
- [X] T061 Run formatting or build verification expected by the repo with `dotnet build Aster.sln`
- [X] T062 Review `git diff --check` output for whitespace and patch formatting issues in `/Users/sipke/Projects/ValenceWorks/aster`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **US1 and US2 (P1)**: Depend on Foundational; can proceed in parallel after T016 if staffed.
- **US3 (P2)**: Depends on Foundational and benefits from US1 failure helpers, but remains independently testable.
- **US4 (P3)**: Depends on the public API names settled by US1 and US2.
- **Polish (Phase 7)**: Depends on desired user stories being complete.

### User Story Dependencies

- **US1**: Can start after Phase 2; no dependency on US2.
- **US2**: Can start after Phase 2; no dependency on US1.
- **US3**: Can start after Phase 2, but should reconcile with US1 structured exception helpers before completion.
- **US4**: Should start after API names and failure shapes are stable.

### Within Each User Story

- Write story tests first and confirm they fail for the missing behavior.
- Implement only the story-specific source changes needed to satisfy those tests.
- Run the story-specific test command before moving to the next story.
- Keep provider-specific checks in place unless a test proves they are duplicate dead code.

## Parallel Execution Examples

### User Story 1

```text
Task: "T017 Add SQLite execution failure assertions in test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs"
Task: "T019 Add in-memory execution failure assertions in test/Aster.Tests/InMemory/InMemoryQueryServiceTests.cs"
Task: "T020 Add supported-query consistency cases in test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs"
```

### User Story 2

```text
Task: "T030 Add custom provider without capabilities test in test/Aster.Tests/Querying/ResourceQueryValidatorTests.cs"
Task: "T032 Add registration-order provider key test in test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs"
Task: "T033 Add provider key assertions in provider capability test files"
```

### User Story 3

```text
Task: "T039 Add SQLite authoritative execution guard tests in test/Aster.Tests/SqliteJson/SqliteJsonQueryServiceTests.cs"
Task: "T040 Add in-memory authoritative execution guard tests in test/Aster.Tests/InMemory/InMemoryQueryServiceTests.cs"
Task: "T041 Add validation-accepts/execution-rejects drift helper coverage in test/Aster.Tests/Querying/QueryCapabilityDiscoveryTests.cs"
```

### User Story 4

```text
Task: "T050 Document query flow in wiki/Querying.md"
Task: "T051 Document provider keys in wiki/DI-Registration.md"
Task: "T052 Document structured exceptions in wiki/Exception-Reference.md"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 so execution failures become structured and consistent.
3. Run US1 tests and stop for validation.

### Incremental Delivery

1. Add explicit provider identity and structured unsupported failures.
2. Deliver US1 for execution feedback consistency.
3. Deliver US2 for fail-closed provider capability matching.
4. Deliver US3 for authoritative provider execution safeguards.
5. Deliver US4 documentation once the final API names are stable.

### Simplicity Guardrails

- Use explicit provider keys and existing DI rather than adding a provider registry framework.
- Keep validation non-throwing and execution throwing.
- Keep provider-specific checks near provider translation/execution code.
- Avoid new third-party dependencies.
