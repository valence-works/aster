# Tasks: Policy Foundations

**Input**: Design documents from `/specs/016-policy-foundations/`  
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/policy-foundations.md](contracts/policy-foundations.md), [quickstart.md](quickstart.md)

**Tests**: Included because the feature specification defines independent tests for every user story and this slice changes policy, persistence, query, and portability behavior.

**Organization**: Tasks are grouped by user story so definition-attached policy declarations can ship as the MVP before preview evaluation and lifecycle marker/query workflows.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on another incomplete task.
- **[Story]**: Maps the task to a user story from [spec.md](spec.md).
- Every task includes an exact file path.

## Phase 1: Setup

**Purpose**: Prepare shared source and test locations for the policy-foundations slice.

- [X] T001 Create policy model folder in `src/core/Aster.Core/Models/Policies/`
- [X] T002 Create policy test folder in `test/Aster.Tests/Policies/`
- [X] T003 [P] Confirm no new package references are required in `src/core/Aster.Core/Aster.Core.csproj`
- [X] T004 [P] Confirm no new package references are required in `src/persistence/Aster.Persistence.SqliteJson/Aster.Persistence.SqliteJson.csproj`

---

## Phase 2: Foundational

**Purpose**: Define shared policy, lifecycle marker, and query surfaces that block every user story.

**Critical**: No user story implementation should begin until the public model and service contracts compile.

- [X] T005 [P] Add policy declaration, criteria, target, outcome, and kind models in `src/core/Aster.Core/Models/Policies/ResourcePolicyModels.cs`
- [X] T006 [P] Add policy validation, preview, candidate outcome, and diagnostic models in `src/core/Aster.Core/Models/Policies/ResourcePolicyResults.cs`
- [X] T007 [P] Add lifecycle marker state, write request, write result, and marker diagnostics in `src/core/Aster.Core/Models/Instances/ResourceLifecycleMarker.cs`
- [X] T008 Add policy declaration collection to resource definitions in `src/core/Aster.Core/Models/Definitions/ResourceDefinition.cs`
- [X] T009 Add lifecycle-state query criterion to the portable query model in `src/core/Aster.Core/Models/Querying/ResourceQuery.cs`
- [X] T010 [P] Add policy validator contract in `src/core/Aster.Core/Abstractions/IResourcePolicyValidator.cs`
- [X] T011 [P] Add policy evaluation contract in `src/core/Aster.Core/Abstractions/IResourcePolicyEvaluationService.cs`
- [X] T012 [P] Add lifecycle marker store and service contracts in `src/core/Aster.Core/Abstractions/IResourceLifecycleMarkerStore.cs`
- [X] T013 Add minimal compiling policy validator, policy evaluation service, in-memory lifecycle marker store, and lifecycle marker service implementations in `src/core/Aster.Core/Services/ResourcePolicyValidator.cs`, `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`, `src/core/Aster.Core/InMemory/InMemoryResourceLifecycleMarkerStore.cs`, and `src/core/Aster.Core/Services/ResourceLifecycleMarkerService.cs`
- [X] T014 Register core policy services and in-memory lifecycle marker services in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs`
- [X] T015 Run foundational compile check with `dotnet build Aster.sln /m:1 --no-restore` using `Aster.sln`

**Checkpoint**: Shared policy and marker contracts compile and can be consumed by user story tasks.

---

## Phase 3: User Story 1 - Declare Resource Policies Explicitly (Priority: P1) MVP

**Goal**: Hosts can declare, inspect, store, and validate retention, archive, soft-delete, and pruning intent as resource definition metadata without changing resource history.

**Independent Test**: Declare policy metadata for a resource type and verify the declaration can be stored, inspected, validated, and reported without changing any resource history.

### Tests for User Story 1

- [X] T016 [P] [US1] Add resource definition policy declaration builder tests in `test/Aster.Tests/Policies/PolicyDeclarationBuilderTests.cs`
- [X] T017 [P] [US1] Add policy validation diagnostics tests in `test/Aster.Tests/Policies/PolicyValidationTests.cs`
- [X] T018 [P] [US1] Add in-memory policy metadata persistence tests in `test/Aster.Tests/Policies/PolicyDefinitionStoreTests.cs`
- [X] T019 [P] [US1] Add SQLite JSON policy metadata persistence tests in `test/Aster.Tests/SqliteJson/SqliteJsonPolicyDefinitionTests.cs`
- [X] T020 [P] [US1] Add no-policy, policy declaration portability, schema upgrade, and lifecycle hook compatibility tests in `test/Aster.Tests/Policies/PolicyCompatibilityTests.cs`

### Implementation for User Story 1

- [X] T021 [US1] Add `WithPolicy` and policy metadata inspection support to `src/core/Aster.Core/Definitions/ResourceDefinitionBuilder.cs`
- [X] T022 [US1] Complete policy declaration validation rules in `src/core/Aster.Core/Services/ResourcePolicyValidator.cs`
- [X] T023 [US1] Add stable policy diagnostic codes in `src/core/Aster.Core/Models/Policies/ResourcePolicyResults.cs`
- [X] T024 [US1] Ensure in-memory definition registration preserves policy declarations in `src/core/Aster.Core/InMemory/InMemoryResourceDefinitionStore.cs`
- [X] T025 [US1] Ensure SQLite definition persistence preserves policy declarations in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T026 [US1] Ensure existing resource create/update flows ignore policy declarations as execution triggers in `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T027 [US1] Add policy declaration snippets and validation guidance in `src/core/Aster.Core/README.md`
- [X] T028 [US1] Run User Story 1 focused tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~PolicyDeclaration|FullyQualifiedName~PolicyValidation|FullyQualifiedName~PolicyDefinition|FullyQualifiedName~PolicyCompatibility"` using `Aster.sln`

**Checkpoint**: User Story 1 is independently functional as the MVP.

---

## Phase 4: User Story 2 - Preview Policy Outcomes Before Action (Priority: P2)

**Goal**: Hosts can request deterministic policy previews for bounded scopes, including archive, soft-delete, and pruning candidates, with diagnostics and no mutations.

**Independent Test**: Create resources and versions that match declared policy conditions, run policy evaluation in preview mode, and verify the preview identifies candidate outcomes without changing stored resources.

### Tests for User Story 2

- [X] T029 [P] [US2] Add archive and soft-delete preview candidate tests in `test/Aster.Tests/Policies/PolicyEvaluationPreviewTests.cs`
- [X] T030 [P] [US2] Add version pruning preview and unsafe pruning diagnostics tests in `test/Aster.Tests/Policies/PolicyPruningPreviewTests.cs`
- [X] T031 [P] [US2] Add explicit evaluation timestamp tests in `test/Aster.Tests/Policies/PolicyEvaluationTimestampTests.cs`
- [X] T032 [P] [US2] Add preview no-mutation tests in `test/Aster.Tests/Policies/PolicyPreviewNoMutationTests.cs`
- [X] T033 [P] [US2] Add tenant-scoped policy preview isolation tests in `test/Aster.Tests/Policies/TenantPolicyEvaluationTests.cs`

### Implementation for User Story 2

- [X] T034 [US2] Complete policy preview orchestration in `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T035 [US2] Implement bounded definition and policy selection in `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T036 [US2] Implement age-based candidate matching with required evaluation timestamps in `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T037 [US2] Implement activation-state and lifecycle-state criteria matching in `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T038 [US2] Implement retained-version count and preview-only pruning candidate selection in `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T039 [US2] Add unsafe pruning and preview-only diagnostics in `src/core/Aster.Core/Services/ResourcePolicyEvaluationService.cs`
- [X] T040 [US2] Ensure policy preview reads tenant-scoped candidate versions through `src/core/Aster.Core/Abstractions/IResourceVersionReader.cs`
- [X] T041 [US2] Ensure policy preview reads lifecycle marker state through the foundational in-memory marker store contract in `src/core/Aster.Core/Abstractions/IResourceLifecycleMarkerStore.cs`
- [X] T042 [US2] Run User Story 2 focused tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~PolicyEvaluation|FullyQualifiedName~PolicyPruning|FullyQualifiedName~PolicyPreview|FullyQualifiedName~TenantPolicy"` using `Aster.sln`

**Checkpoint**: User Stories 1 and 2 are independently functional.

---

## Phase 5: User Story 3 - Mark And Query Resource Lifecycle State (Priority: P3)

**Goal**: Hosts can explicitly apply archive and soft-delete markers, inspect/query lifecycle state, and preserve markers through portability without hidden filtering or resource history rewrites.

**Independent Test**: Explicitly apply archive and soft-delete markers to resources, then verify normal resource history remains intact and hosts can query or inspect the lifecycle state through explicit criteria.

### Tests for User Story 3

- [X] T043 [P] [US3] Add lifecycle marker write and idempotency tests in `test/Aster.Tests/Policies/LifecycleMarkerServiceTests.cs`
- [X] T044 [P] [US3] Add lifecycle marker conflict diagnostic tests in `test/Aster.Tests/Policies/LifecycleMarkerConflictTests.cs`
- [X] T045 [P] [US3] Add in-memory lifecycle-state query tests in `test/Aster.Tests/Querying/LifecycleStateQueryTests.cs`
- [X] T046 [P] [US3] Add SQLite JSON lifecycle marker persistence and query tests in `test/Aster.Tests/SqliteJson/SqliteJsonLifecycleMarkerTests.cs`
- [X] T047 [P] [US3] Add lifecycle marker portability export/import tests in `test/Aster.Tests/Portability/PortabilityLifecycleMarkerTests.cs`
- [X] T048 [P] [US3] Add tenant-scoped lifecycle marker isolation tests in `test/Aster.Tests/Tenancy/TenantLifecycleMarkerTests.cs`

### Implementation for User Story 3

- [X] T049 [US3] Complete in-memory lifecycle marker storage in `src/core/Aster.Core/InMemory/InMemoryResourceLifecycleMarkerStore.cs`
- [X] T050 [US3] Complete lifecycle marker service validation, idempotency, and conflict handling in `src/core/Aster.Core/Services/ResourceLifecycleMarkerService.cs`
- [X] T051 [US3] Add lifecycle marker SQLite schema support in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`
- [X] T052 [US3] Implement SQLite lifecycle marker store operations and registration support in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs` and `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonAsterServiceCollectionExtensions.cs`
- [X] T053 [US3] Add lifecycle-state capability declaration to in-memory query capabilities in `src/core/Aster.Core/InMemory/InMemoryQueryCapabilitiesProvider.cs`
- [X] T054 [US3] Add lifecycle-state capability declaration to SQLite query capabilities in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryCapabilitiesProvider.cs`
- [X] T055 [US3] Validate lifecycle-state query criteria in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T056 [US3] Apply lifecycle-state filtering in in-memory queries in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T057 [US3] Apply lifecycle-state filtering in SQLite query execution in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [X] T058 [US3] Add lifecycle marker state to portable snapshots and store models in `src/core/Aster.Core/Models/Portability/PortableSnapshot.cs`
- [X] T059 [US3] Preserve lifecycle marker state in portability export, preview import, and write import in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T060 [US3] Preserve lifecycle marker state in in-memory portability storage in `src/core/Aster.Core/InMemory/InMemoryPortabilityStore.cs`
- [X] T061 [US3] Preserve lifecycle marker state in SQLite portability storage in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T062 [US3] Run User Story 3 focused tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~LifecycleMarker|FullyQualifiedName~LifecycleStateQuery|FullyQualifiedName~PortabilityLifecycleMarker|FullyQualifiedName~TenantLifecycleMarker"` using `Aster.sln`

**Checkpoint**: All policy-foundations user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, compatibility, cleanup, and full verification.

- [X] T063 [P] Update SQLite JSON lifecycle marker registration and provider notes in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T064 [P] Validate policy quickstart snippets against implemented APIs in `specs/016-policy-foundations/quickstart.md`
- [X] T065 [P] Update roadmap/status docs for policy foundations in `docs/ExecutionRoadmap.md`
- [X] T066 [P] Update public roadmap docs for policy foundations in `docs/Roadmap.md`
- [X] T067 Re-run Constitution Check and remove unnecessary policy abstractions in `specs/016-policy-foundations/plan.md`
- [X] T068 Run all tests with `dotnet test Aster.sln --no-restore` using `Aster.sln`
- [X] T069 Run full build with `dotnet build Aster.sln /m:1 --no-restore` using `Aster.sln`
- [X] T070 Run whitespace validation with `git diff --check` using `/Users/sipke/Projects/ValenceWorks/aster`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and practically follows US1 because previews evaluate persisted policy declarations.
- **User Story 3 (Phase 5)**: Depends on Foundational and can proceed after marker contracts exist; query and portability pieces integrate with existing stores.
- **Polish (Phase 6)**: Depends on all desired user stories.

### User Story Dependencies

- **US1 Declare Resource Policies Explicitly**: Start after Foundational; validates declaration metadata, storage, inspection, and diagnostics.
- **US2 Preview Policy Outcomes Before Action**: Start after Foundational; practically follows US1 for persisted declaration storage and validation reuse.
- **US3 Mark And Query Resource Lifecycle State**: Start after Foundational; can be implemented independently of US2, though policy preview can later read marker state.

### Within Each User Story

- Write story tests first and verify they fail.
- Implement models/contracts before service/provider behavior.
- Implement in-memory behavior before SQLite provider behavior when both are required.
- Run focused story tests at the checkpoint before moving to the next story.

### Parallel Opportunities

- Setup tasks T003-T004 can run in parallel.
- Foundational model and contract tasks T005-T007 and T010-T012 can run in parallel.
- User Story 1 tests T016-T020 can be written in parallel.
- User Story 2 tests T029-T033 can be written in parallel.
- User Story 3 tests T043-T048 can be written in parallel.
- Documentation tasks T063-T066 can be completed in parallel after implementation stabilizes.

---

## Parallel Example: User Story 1

```text
Task: "Add resource definition policy declaration builder tests in test/Aster.Tests/Policies/PolicyDeclarationBuilderTests.cs"
Task: "Add policy validation diagnostics tests in test/Aster.Tests/Policies/PolicyValidationTests.cs"
Task: "Add in-memory policy metadata persistence tests in test/Aster.Tests/Policies/PolicyDefinitionStoreTests.cs"
Task: "Add SQLite JSON policy metadata persistence tests in test/Aster.Tests/SqliteJson/SqliteJsonPolicyDefinitionTests.cs"
Task: "Add no-policy compatibility tests in test/Aster.Tests/Policies/PolicyCompatibilityTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "Add archive and soft-delete preview candidate tests in test/Aster.Tests/Policies/PolicyEvaluationPreviewTests.cs"
Task: "Add version pruning preview and unsafe pruning diagnostics tests in test/Aster.Tests/Policies/PolicyPruningPreviewTests.cs"
Task: "Add explicit evaluation timestamp tests in test/Aster.Tests/Policies/PolicyEvaluationTimestampTests.cs"
Task: "Add preview no-mutation tests in test/Aster.Tests/Policies/PolicyPreviewNoMutationTests.cs"
Task: "Add tenant-scoped policy preview isolation tests in test/Aster.Tests/Policies/TenantPolicyEvaluationTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "Add lifecycle marker write and idempotency tests in test/Aster.Tests/Policies/LifecycleMarkerServiceTests.cs"
Task: "Add lifecycle marker conflict diagnostic tests in test/Aster.Tests/Policies/LifecycleMarkerConflictTests.cs"
Task: "Add in-memory lifecycle-state query tests in test/Aster.Tests/Querying/LifecycleStateQueryTests.cs"
Task: "Add SQLite JSON lifecycle marker persistence and query tests in test/Aster.Tests/SqliteJson/SqliteJsonLifecycleMarkerTests.cs"
Task: "Add lifecycle marker portability export/import tests in test/Aster.Tests/Portability/PortabilityLifecycleMarkerTests.cs"
Task: "Add tenant-scoped lifecycle marker isolation tests in test/Aster.Tests/Tenancy/TenantLifecycleMarkerTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 setup.
2. Complete Phase 2 foundational policy and marker contracts.
3. Complete Phase 3 User Story 1.
4. Stop and validate policy declaration storage, inspection, and diagnostics independently.

### Incremental Delivery

1. Deliver US1 to establish definition-attached policy declarations and validation.
2. Add US2 to prove deterministic preview behavior without writes.
3. Add US3 to add explicit lifecycle marker writes, querying, and portability.
4. Finish with docs, quickstart validation, full tests, full build, and whitespace validation.

### Quality Bar

- Preserve append-only resource versions throughout.
- Require explicit host input for policy previews and marker writes.
- Keep lifecycle filtering explicit; do not hide archived or soft-deleted resources by default.
- Do not add automatic execution, scheduler, policy engine, provider registry, runtime scanning, public SQL, public `IQueryable<Resource>`, destructive pruning writes, or restore workflows.
- Prefer direct SDK records and provider-local storage over broad framework abstractions.
