# Tasks: Tenant Scoping

**Input**: Design documents from `/specs/015-tenant-scoping/`  
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/tenant-scoping.md](contracts/tenant-scoping.md), [quickstart.md](quickstart.md)

**Tests**: Included because the feature specification defines independent tests for every user story and this slice changes isolation semantics.

**Organization**: Tasks are grouped by user story so tenant definition/resource isolation can ship as the MVP before tenant-aware query and integration workflows.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on another incomplete task.
- **[Story]**: Maps the task to a user story from [spec.md](spec.md).
- Every task includes an exact file path.

## Phase 1: Setup

**Purpose**: Prepare shared test and source locations for the tenant-scoping slice.

- [X] T001 Create tenant test folder and shared fixture placeholder in `test/Aster.Tests/Tenancy/TenantScopeTestFixtures.cs`
- [X] T002 Create tenant model folder for SDK tenancy types in `src/core/Aster.Core/Models/Tenancy/`
- [X] T003 Confirm no new package references are required in `src/core/Aster.Core/Aster.Core.csproj`
- [X] T004 Confirm no new package references are required in `src/persistence/Aster.Persistence.SqliteJson/Aster.Persistence.SqliteJson.csproj`

---

## Phase 2: Foundational

**Purpose**: Define tenant scope primitives and request/context surfaces that block every user story.

**Critical**: No user story implementation should begin until tenant scope resolution and public request shape are in place.

- [X] T005 [P] Add tenant scope value-object tests in `test/Aster.Tests/Tenancy/TenantScopeTests.cs`
- [X] T006 [P] Add tenant scope failure tests in `test/Aster.Tests/Tenancy/TenantScopeFailureTests.cs`
- [X] T007 Implement `TenantScope` with default-scope resolution and opaque exact-match validation in `src/core/Aster.Core/Models/Tenancy/TenantScope.cs`
- [X] T008 Implement stable tenant-scope failure exception or diagnostic model in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`
- [X] T009 Add central tenant scope resolver/validator in `src/core/Aster.Core/Services/TenantScopeResolver.cs`
- [X] T010 Add tenant scope properties to existing create/update request DTOs in `src/core/Aster.Core/Abstractions/Requests.cs`
- [X] T011 Add tenant scope to version read requests in `src/core/Aster.Core/Abstractions/IResourceVersionReader.cs`
- [X] T012 Add tenant scope to query model in `src/core/Aster.Core/Models/Querying/ResourceQuery.cs`
- [X] T013 Add tenant scope metadata to lifecycle context base record in `src/core/Aster.Core/Models/Lifecycle/LifecycleHookContexts.cs`
- [X] T014 Add tenant scope metadata to portability snapshot, export request, and import options in `src/core/Aster.Core/Models/Portability/PortableSnapshot.cs`
- [X] T015 Add tenant scope metadata to portability result models in `src/core/Aster.Core/Models/Portability/PortableResults.cs`
- [X] T016 Run focused foundational tenant tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~TenantScope"` using `Aster.sln`

**Checkpoint**: Tenant scope primitives compile, validate, and can be carried by shared SDK requests/contexts.

---

## Phase 3: User Story 1 - Isolate Definitions And Resources By Tenant (Priority: P1) MVP

**Goal**: Definitions, resources, versions, and activation state are isolated by tenant while omitted scope remains default single-tenant behavior.

**Independent Test**: Create two tenants with identical definition/resource IDs and verify create, update, read, activate, deactivate, and default-scope compatibility never cross tenant boundaries.

### Tests for User Story 1

- [X] T017 [P] [US1] Add tenant-scoped definition store tests in `test/Aster.Tests/Tenancy/TenantDefinitionStoreTests.cs`
- [X] T018 [P] [US1] Add tenant-scoped resource lifecycle tests in `test/Aster.Tests/Tenancy/TenantResourceManagerTests.cs`
- [X] T019 [P] [US1] Add tenant-scoped activation tests in `test/Aster.Tests/Tenancy/TenantActivationTests.cs`
- [X] T020 [P] [US1] Add default single-tenant compatibility tests in `test/Aster.Tests/Tenancy/TenantDefaultScopeCompatibilityTests.cs`
- [X] T021 [P] [US1] Add SQLite JSON tenant isolation and pre-tenant default-scope compatibility tests in `test/Aster.Tests/SqliteJson/SqliteJsonTenantScopeTests.cs`

### Implementation for User Story 1

- [X] T022 [US1] Add additive tenant-aware overloads while preserving default-scope methods in `src/core/Aster.Core/Abstractions/IResourceDefinitionStore.cs`
- [X] T023 [US1] Implement tenant-partitioned definition storage in `src/core/Aster.Core/InMemory/InMemoryResourceDefinitionStore.cs`
- [X] T024 [US1] Add additive tenant-aware overloads for method-style resource manager APIs while using request tenant fields for create/update in `src/core/Aster.Core/Abstractions/IResourceManager.cs`
- [X] T025 [US1] Implement tenant scope flow for create/update/read/activation in `src/core/Aster.Core/Services/DefaultResourceManager.cs`
- [X] T026 [US1] Implement tenant-partitioned resource versions and activation state in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs`
- [X] T027 [US1] Add tenant-aware SQLite table shape and idempotent default-scope compatibility bootstrap for existing databases in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonSchema.cs`
- [X] T028 [US1] Implement tenant-scoped definition/resource/activation reads and writes in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T029 [US1] Preserve default-scope behavior in web sample setup in `src/apps/Aster.Web/SeedDataInitializer.cs`
- [X] T030 [US1] Run User Story 1 focused tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~TenantDefinitionStore|FullyQualifiedName~TenantResourceManager|FullyQualifiedName~TenantActivation|FullyQualifiedName~TenantDefaultScopeCompatibility|FullyQualifiedName~SqliteJsonTenantScope"` using `Aster.sln`

**Checkpoint**: User Story 1 is independently functional as the MVP.

---

## Phase 4: User Story 2 - Query Within An Explicit Tenant Boundary (Priority: P2)

**Goal**: In-memory and SQLite JSON queries resolve one effective tenant and never return resources from another tenant.

**Independent Test**: Store matching resources in multiple tenants, run metadata, activation, and facet queries for one tenant, and verify only that tenant's results are returned.

### Tests for User Story 2

- [X] T031 [P] [US2] Add tenant-aware query validation tests in `test/Aster.Tests/Querying/TenantQueryValidatorTests.cs`
- [X] T032 [P] [US2] Add in-memory tenant query isolation tests in `test/Aster.Tests/Tenancy/TenantQueryServiceTests.cs`
- [X] T033 [P] [US2] Add SQLite JSON tenant query isolation tests in `test/Aster.Tests/SqliteJson/SqliteJsonTenantQueryServiceTests.cs`
- [X] T034 [P] [US2] Add active-channel tenant query isolation tests in `test/Aster.Tests/Tenancy/TenantActiveQueryTests.cs`

### Implementation for User Story 2

- [X] T035 [US2] Validate query tenant scope before provider capability traversal in `src/core/Aster.Core/Services/ResourceQueryValidator.cs`
- [X] T036 [US2] Apply tenant scope before query predicates in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [X] T037 [US2] Pass tenant scope through candidate version reads in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs`
- [X] T038 [US2] Add tenant predicates to SQLite query builder inputs in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonQueryService.cs`
- [X] T039 [US2] Add tenant filter SQL support in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteQueryBuilder.cs`
- [X] T040 [US2] Add tenant filter translation support in `src/persistence/Aster.Persistence.SqliteJson/Querying/SqliteWhereTranslator.cs`
- [X] T041 [US2] Run User Story 2 focused tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~TenantQuery|FullyQualifiedName~SqliteJsonTenantQuery"` using `Aster.sln`

**Checkpoint**: User Stories 1 and 2 are independently functional.

---

## Phase 5: User Story 3 - Keep Integration Workflows Tenant-Scoped (Priority: P3)

**Goal**: Schema upgrades, portability, and lifecycle hooks carry tenant scope and preserve single-source/single-target tenant behavior.

**Independent Test**: Run schema upgrade, export, import preview, write import, and lifecycle hook flows for one tenant while another tenant contains similar data, then verify only the selected tenant participates.

### Tests for User Story 3

- [X] T042 [P] [US3] Add tenant-scoped schema upgrade tests in `test/Aster.Tests/SchemaVersions/TenantSchemaVersionServiceTests.cs`
- [X] T043 [P] [US3] Add tenant-scoped portability export tests in `test/Aster.Tests/Portability/TenantPortabilityExportTests.cs`
- [X] T044 [P] [US3] Add tenant-scoped portability preview/import tests in `test/Aster.Tests/Portability/TenantPortabilityImportTests.cs`
- [X] T045 [P] [US3] Add tenant lifecycle hook context tests in `test/Aster.Tests/Lifecycle/TenantLifecycleHookTests.cs`
- [X] T046 [P] [US3] Add SQLite JSON tenant portability tests in `test/Aster.Tests/SqliteJson/SqliteJsonTenantPortabilityStoreTests.cs`

### Implementation for User Story 3

- [X] T047 [US3] Resolve definition lineage within tenant scope in `src/core/Aster.Core/Services/ResourceSchemaVersionService.cs`
- [X] T048 [US3] Add source tenant and target tenant diagnostics constants in `src/core/Aster.Core/Models/Portability/PortableDiagnostic.cs`
- [X] T049 [US3] Enforce single-source snapshot validation in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T050 [US3] Enforce explicit target tenant import preview/write behavior in `src/core/Aster.Core/Services/ResourcePortabilityService.cs`
- [X] T051 [US3] Add tenant-scoped export/import store behavior in `src/core/Aster.Core/InMemory/InMemoryPortabilityStore.cs`
- [X] T052 [US3] Add tenant-scoped SQLite export/import store behavior in `src/persistence/Aster.Persistence.SqliteJson/SqliteJsonResourceStore.cs`
- [X] T053 [US3] Populate tenant scope in lifecycle hook context snapshots in `src/core/Aster.Core/Services/ResourceLifecycleHookContextSnapshots.cs`
- [X] T054 [US3] Ensure lifecycle dispatcher preserves tenant-scoped context values in `src/core/Aster.Core/Services/ResourceLifecycleHookDispatcher.cs`
- [X] T055 [US3] Run User Story 3 focused tests with `dotnet test Aster.sln --no-restore --filter "FullyQualifiedName~TenantSchema|FullyQualifiedName~TenantPortability|FullyQualifiedName~TenantLifecycle|FullyQualifiedName~SqliteJsonTenantPortability"` using `Aster.sln`

**Checkpoint**: All tenant-scoping user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, compatibility, cleanup, and full verification.

- [X] T056 [P] Update core tenant-scoping guidance in `src/core/Aster.Core/README.md`
- [X] T057 [P] Update SQLite JSON provider tenant notes in `src/persistence/Aster.Persistence.SqliteJson/README.md`
- [X] T058 [P] Update public roadmap/status docs for tenant scoping in `README.md`
- [X] T059 [P] Validate tenant quickstart snippets against implemented APIs in `specs/015-tenant-scoping/quickstart.md`
- [X] T060 Re-run Constitution Check and remove unnecessary tenant abstractions in `specs/015-tenant-scoping/plan.md`
- [X] T061 Run all tests with `dotnet test Aster.sln --no-restore` using `Aster.sln`
- [X] T062 Run full build with `dotnet build Aster.sln /m:1 --no-restore` using `Aster.sln`
- [X] T063 Run whitespace validation with `git diff --check` using `/Users/sipke/Projects/ValenceWorks/aster`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and benefits from User Story 1 store partitioning.
- **User Story 3 (Phase 5)**: Depends on Foundational and benefits from User Story 1 scoped lifecycle/storage behavior.
- **Polish (Phase 6)**: Depends on all desired user stories.

### User Story Dependencies

- **US1 Isolate Definitions And Resources By Tenant**: Start after Foundational; validates core write/read isolation.
- **US2 Query Within An Explicit Tenant Boundary**: Start after Foundational; practically follows US1 because query candidates come from tenant-scoped stores.
- **US3 Keep Integration Workflows Tenant-Scoped**: Start after Foundational; practically follows US1 because schema upgrades, portability, and hooks rely on scoped lifecycle/storage data.

### Within Each User Story

- Write story tests first and verify they fail.
- Implement core models/contracts before service/provider behavior.
- Implement in-memory behavior before SQLite provider behavior when both are required.
- Run focused story tests at the checkpoint before moving to the next story.

### Parallel Opportunities

- Setup tasks T001-T004 can be split by file.
- Foundational tests T005-T006 can be written in parallel.
- User Story 1 tests T017-T021 can be written in parallel.
- User Story 2 tests T031-T034 can be written in parallel.
- User Story 3 tests T042-T046 can be written in parallel.
- Documentation tasks T056-T059 can be completed in parallel after implementation stabilizes.

---

## Parallel Example: User Story 1

```text
Task: "Add tenant-scoped definition store tests in test/Aster.Tests/Tenancy/TenantDefinitionStoreTests.cs"
Task: "Add tenant-scoped resource lifecycle tests in test/Aster.Tests/Tenancy/TenantResourceManagerTests.cs"
Task: "Add tenant-scoped activation tests in test/Aster.Tests/Tenancy/TenantActivationTests.cs"
Task: "Add default single-tenant compatibility tests in test/Aster.Tests/Tenancy/TenantDefaultScopeCompatibilityTests.cs"
Task: "Add SQLite JSON tenant definition/resource isolation tests in test/Aster.Tests/SqliteJson/SqliteJsonTenantScopeTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "Add tenant-aware query validation tests in test/Aster.Tests/Querying/TenantQueryValidatorTests.cs"
Task: "Add in-memory tenant query isolation tests in test/Aster.Tests/Tenancy/TenantQueryServiceTests.cs"
Task: "Add SQLite JSON tenant query isolation tests in test/Aster.Tests/SqliteJson/SqliteJsonTenantQueryServiceTests.cs"
Task: "Add active-channel tenant query isolation tests in test/Aster.Tests/Tenancy/TenantActiveQueryTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "Add tenant-scoped schema upgrade tests in test/Aster.Tests/SchemaVersions/TenantSchemaVersionServiceTests.cs"
Task: "Add tenant-scoped portability export tests in test/Aster.Tests/Portability/TenantPortabilityExportTests.cs"
Task: "Add tenant-scoped portability preview/import tests in test/Aster.Tests/Portability/TenantPortabilityImportTests.cs"
Task: "Add tenant lifecycle hook context tests in test/Aster.Tests/Lifecycle/TenantLifecycleHookTests.cs"
Task: "Add SQLite JSON tenant portability tests in test/Aster.Tests/SqliteJson/SqliteJsonTenantPortabilityStoreTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 setup.
2. Complete Phase 2 foundational tenant scope model/request/context work.
3. Complete Phase 3 User Story 1.
4. Stop and validate tenant definition/resource/activation isolation independently.

### Incremental Delivery

1. Deliver US1 to establish scoped definitions, resources, versions, and activation state.
2. Add US2 to prove queries cannot leak across tenants.
3. Add US3 to carry tenant scope through schema upgrades, portability, and lifecycle hooks.
4. Finish with docs, quickstart validation, full tests, full build, and whitespace validation.

### Quality Bar

- Preserve default single-tenant compatibility throughout.
- Keep tenant behavior explicit in request/context models.
- Do not add ambient tenant context, tenant registry, authorization, policy engine, migration framework, public SQL, or public `IQueryable<Resource>`.
- Prefer direct scoped keys and provider filters over broad framework abstractions.
