# Tasks: Core SDK Foundation

**Input**: Design documents from `specs/001-core-sdk-foundation/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/Abstractions.cs ✅, quickstart.md ✅

**Organization**: Tasks are grouped by user story (US1–US5) to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Exact C# file paths are in `src/core/Aster.Core/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the `Aster.Core` project, apply coding conventions, and wire up the solution.

- [ ] T001 Create `src/core/Aster.Core/Aster.Core.csproj` targeting `net8.0;net9.0;net10.0` with `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Logging.Abstractions` references
- [ ] T002 Add `Aster.Core` to `Aster.sln` and reference it from `src/apps/Aster.Web/Aster.Web.csproj`
- [ ] T003 [P] Add NSubstitute package to `test/Aster.Tests/Aster.Tests.csproj` and add project reference to `Aster.Core`
- [ ] T004 [P] Create folder skeleton: `src/core/Aster.Core/{Abstractions,Models,Definitions,Services,InMemory,Extensions}/`

**Checkpoint**: Solution builds; test project references core.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain models and contracts that every user story depends on. No user story can begin until all of these exist.

**⚠️ CRITICAL**: Complete before any Phase 3+ work.

- [ ] T005 Create `ResourceDefinition` record in `src/core/Aster.Core/Models/Definitions/ResourceDefinition.cs` (DefinitionId, Id, Version, AspectDefinitions, IsSingleton) — universal versioning pattern: `DefinitionId` = logical, `Id` = version-specific
- [ ] T005a [P] Create `FacetDefinition` record in `src/core/Aster.Core/Models/Definitions/FacetDefinition.cs` (FacetDefinitionId, Id, Version, Type, IsRequired) — logical id = `FacetDefinitionId`
- [ ] T006 [P] Create `AspectDefinition` record in `src/core/Aster.Core/Models/Definitions/AspectDefinition.cs` (AspectDefinitionId, Id, Version, RequiresName, Schema, FacetDefinitions) — logical id = `AspectDefinitionId`
- [ ] T007 Create merged `Resource` record in `src/core/Aster.Core/Models/Instances/Resource.cs` (ResourceId, Id, DefinitionId, DefinitionVersion?, Version, Created, Owner?, Aspects, Hash?) — replaces both former `Resource` identity and `ResourceVersion` snapshot; `ResourceId` = logical, `Id` = version-specific; immutable
- [ ] T007a [P] Create `AspectInstance` record in `src/core/Aster.Core/Models/Instances/AspectInstance.cs` (AspectDefinitionId, Name, Facets) — spec §3.1; unnamed key = `AspectDefinitionId`, named key = `"{AspectDefinitionId}:{Name}"` composite
- [ ] T007b [P] Create `FacetValue` record in `src/core/Aster.Core/Models/Instances/FacetValue.cs` (FacetDefinitionId, Value) — spec §3.1; used in Query AST FacetValue filter expressions
- [ ] T008 Define `ITypedFacetBinder` interface (bind a POCO to a `FacetDefinitionId` key; serialize/deserialize single facet value) in `src/core/Aster.Core/Abstractions/ITypedFacetBinder.cs` — mirrors `ITypedAspectBinder` at the facet level (spec §3.4 Typed Facets)
- [ ] T009 [P] Create `ActivationState` record in `src/core/Aster.Core/Models/Instances/ActivationState.cs` (ResourceId, Channel, ActiveVersions, LastUpdated)
- [ ] T010 Create request/DTO types `CreateResourceRequest` and `UpdateResourceRequest` (with `BaseVersion` for optimistic locking) in `src/core/Aster.Core/Abstractions/Requests.cs`
- [ ] T011 Define custom exceptions: `VersionNotFoundException`, `ConcurrencyException`, `DuplicateAspectAttachmentException`, `DuplicateResourceIdException`, `SingletonViolationException` in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`
- [ ] T012 Transcribe `IResourceDefinitionStore` contract (including `GetDefinitionVersionAsync`) from `contracts/Abstractions.cs` into `src/core/Aster.Core/Abstractions/IResourceDefinitionStore.cs`
- [ ] T012a [P] Define `IIdentityGenerator` contract in `src/core/Aster.Core/Abstractions/IIdentityGenerator.cs` and implement `GuidIdentityGenerator` (default, `Guid.NewGuid().ToString()`) in `src/core/Aster.Core/Services/GuidIdentityGenerator.cs`
- [ ] T013 [P] Transcribe `IResourceManager` contract (including activation methods and `GetLatestVersionAsync`) from `contracts/Abstractions.cs` into `src/core/Aster.Core/Abstractions/IResourceManager.cs`
- [ ] T013a Define `IResourceWriteStore` contract (`SaveVersionAsync`, `UpdateActivationAsync`) in `src/core/Aster.Core/Abstractions/IResourceWriteStore.cs` — required by Constitution Principle V (Provider Agnostic); `Resource` IS a version so no separate `WriteVersionAsync` needed

**Checkpoint**: Solution compiles with all shared types and contracts defined. User stories can now proceed in parallel.

---

## Phase 3: User Story 1 — Defining a Resource Type / Code-First (Priority: P1) 🎯 MVP

**Goal**: A developer can fluently define a resource type with named/unnamed aspect attachments and register it in the definition store.

**User Scenario**: §2.1 — Developer uses `IResourceDefinitionBuilder` to define "Product" with `TitleAspect`, `PriceAspect`, and named `TagAspect` instances; system registers the definition.

**Independent Test**: Instantiate `ResourceDefinitionBuilder`, build a definition with duplicate aspects, confirm `DuplicateAspectAttachmentException` is thrown; build a valid definition and register it via `InMemoryResourceDefinitionStore`, then read it back.

- [ ] T014 [US1] Create `ResourceDefinitionBuilder` with fluent `.WithId()`, `.WithAspect<T>()`, `.WithNamedAspect<T>(name)`, and `.Build()` methods in `src/core/Aster.Core/Definitions/ResourceDefinitionBuilder.cs`
- [ ] T015 [US1] Add uniqueness validation (duplicate unnamed/named attachment check, throws `DuplicateAspectAttachmentException`) inside `ResourceDefinitionBuilder.Build()` in `src/core/Aster.Core/Definitions/ResourceDefinitionBuilder.cs` — key scheme: unnamed = `AspectDefinition.AspectDefinitionId`; named = `"{AspectDefinitionId}:{Name}"` composite (see data-model.md)
- [ ] T016 [US1] Implement `InMemoryResourceDefinitionStore` (`IResourceDefinitionStore`) using `ConcurrentDictionary<string, List<ResourceDefinition>>` (key = `Id`, list = ordered versions) in `src/core/Aster.Core/InMemory/InMemoryResourceDefinitionStore.cs`; `RegisterDefinitionAsync` appends and auto-increments `Version`; `GetDefinitionAsync` returns last entry; `ListDefinitionsAsync` returns latest per `Id`
- [ ] T017 [P] [US1] Write unit tests for `ResourceDefinitionBuilder` (valid definition, duplicate aspect, named aspect) in `test/Aster.Tests/Definitions/ResourceDefinitionBuilderTests.cs`
- [ ] T018 [P] [US1] Write unit tests for `InMemoryResourceDefinitionStore` (register first version, register second version auto-increments, get latest, get specific version, list returns latest per Id) in `test/Aster.Tests/InMemory/InMemoryResourceDefinitionStoreTests.cs`

**Checkpoint**: User Story 1 fully implemented and tested independently.

---

## Phase 4: User Story 2 — Creating and Versioning a Resource (Priority: P1)

**Goal**: A developer can create a resource, update it producing a new immutable version, and activate a specific version in a named channel with optimistic concurrency enforcement.

**User Scenario**: §2.2 — User creates Product (V1 Draft), updates title to produce V2, activates V2 in "Published"; V1 remains inactive. §2.5 edge cases: `VersionNotFoundException`, `ConcurrencyException`.

**Independent Test**: Create a resource via `InMemoryResourceManager`, update it twice (verifying version numbers increment), attempt to activate a non-existent version (expect `VersionNotFoundException`), simulate concurrent update (expect `ConcurrencyException`).

- [ ] T019 Implement `InMemoryResourceStore` (private storage) using `ConcurrentDictionary<string, List<Resource>>` (key = `ResourceId`; list = ordered version history) and `ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<int>>>` for channel activations (key = `ResourceId` → channel → active version numbers) in `src/core/Aster.Core/InMemory/InMemoryResourceStore.cs` — no separate Resource identity collection; `ResourceId` from first `Resource.ResourceId`
- [ ] T020 Implement `IResourceManager` as `InMemoryResourceManager` with `CreateAsync` (produces V1; resolves ID via `IIdentityGenerator` when `CreateResourceRequest.Id` is null; throws `DuplicateResourceIdException` if caller-supplied ID already exists; throws `SingletonViolationException` if `IsSingleton` and instance exists) and `UpdateAsync` (increments version, enforces `BaseVersion` ETag via `ConcurrencyException`) in `src/core/Aster.Core/InMemory/InMemoryResourceManager.cs`
- [ ] T021 [US2] Add `GetVersionAsync`, `GetVersionsAsync`, and `GetLatestVersionAsync` to `InMemoryResourceManager` in `src/core/Aster.Core/InMemory/InMemoryResourceManager.cs`
- [ ] T022 [US2] Implement `ActivateAsync` with `allowMultipleActive` flag (single-active deactivates others, multi-active appends) and `DeactivateAsync` in `InMemoryResourceManager` in `src/core/Aster.Core/InMemory/InMemoryResourceManager.cs`
- [ ] T023 [US2] Implement `GetActiveVersionsAsync` in `InMemoryResourceManager` in `src/core/Aster.Core/InMemory/InMemoryResourceManager.cs`
- [ ] T024 [P] [US2] Write unit tests for create/update/versioning (version increment, immutability, optimistic lock, singleton enforcement, duplicate ID) in `test/Aster.Tests/InMemory/InMemoryResourceManagerTests.cs`
- [ ] T025 [P] [US2] Write unit tests for activation (single-active, multi-active, `VersionNotFoundException`, `ConcurrencyException`, deactivation) in `test/Aster.Tests/InMemory/InMemoryActivationTests.cs`

**Checkpoint**: User Story 2 fully implemented and tested independently. Full create → version → activate flow works.

---

## Phase 5: User Story 3 — Using Typed Aspects (POCOs) (Priority: P2)

**Goal**: A developer can register a C# record as an aspect type and seamlessly read/write it from a `ResourceVersion` with State Replace semantics using `System.Text.Json`.

**User Scenario**: §2.3 — Developer defines `PriceAspect(decimal Amount, string Currency)`, registers it, loads a resource version, requests the POCO, modifies it, saves; system round-trips via JSON.

**Independent Test**: Define a `TitleAspect` POCO, create a resource with a typed aspect, retrieve and deserialize the POCO, mutate and save back, confirm the serialized dictionary in `ResourceVersion.Aspects` reflects the new values (State Replace confirmed).

- [ ] T026 [US3] Define `ITypedAspectBinder` interface (bind POCO to aspect key, serialize/deserialize full aspect) in `src/core/Aster.Core/Abstractions/ITypedAspectBinder.cs`
- [ ] T026a [P] [US3] Implement `ITypedFacetBinder` (defined in T008) as `SystemTextJsonFacetBinder` using `System.Text.Json`; State Replace semantics for single facet value in `src/core/Aster.Core/Services/SystemTextJsonFacetBinder.cs`
- [ ] T027 [US3] Implement `SystemTextJsonAspectBinder` (`ITypedAspectBinder`) using `System.Text.Json` with State Replace logic in `src/core/Aster.Core/Services/SystemTextJsonAspectBinder.cs`
- [ ] T028 [P] [US3] Add `GetAspect<T>` and `SetAspect<T>` extension methods on `Resource` in `src/core/Aster.Core/Extensions/ResourceExtensions.cs`
- [ ] T028a [P] [US3] Add `GetFacet<T>` and `SetFacet<T>` extension methods on `AspectInstance` in `src/core/Aster.Core/Extensions/AspectInstanceExtensions.cs` (spec §3.4 Typed Facets)
- [ ] T029 [P] [US3] Add `WithTypedAspect<T>()` and `WithTypedFacet<T>()` registration methods to `ResourceDefinitionBuilder` in `src/core/Aster.Core/Definitions/ResourceDefinitionBuilder.cs`
- [ ] T030 [US3] Write unit tests for aspect POCO round-trip (string, int, bool, decimal, date), State Replace semantics, unknown-field handling, AND facet-level POCO round-trip in `test/Aster.Tests/Services/SystemTextJsonAspectBinderTests.cs`

**Checkpoint**: User Story 3 fully implemented. Typed Aspects work end-to-end without breaking US1/US2.

---

## Phase 6: User Story 4 — Basic In-Memory Querying (Priority: P2)

**Goal**: A developer can construct a portable query AST and execute it against the in-memory store to retrieve filtered resources.

**User Scenario**: §2.4 — Developer constructs `ResourceQuery` filtering by `ResourceType == 'Product'` and `TitleAspect.Title contains 'Gadget'`; query executes via `IResourceQueryService` and returns matching resources.

**Independent Test**: Seed the in-memory store with 3 Product resources and 1 Order resource; query for Products where Title contains "Gadget"; confirm only the expected resources are returned.

- [ ] T031 [US4] Define Query AST types: `ResourceQuery`, `FilterExpression` (Metadata, AspectPresence, FacetValue filters), `LogicalExpression` (AND/OR/NOT), `ComparisonOperator` (Equals, Contains, Range) in `src/core/Aster.Core/Models/Querying/` — **Note**: Range is included in the AST contract but the Phase 1 in-memory evaluator (T033) MUST throw `NotSupportedException` for Range (spec §6 scope: Equals + Contains only)
- [ ] T032 [US4] Define `IResourceQueryService` contract in `src/core/Aster.Core/Abstractions/IResourceQueryService.cs`
- [ ] T033 [US4] Implement `InMemoryQueryService` (`IResourceQueryService`) using LINQ to Objects translation of the AST in `src/core/Aster.Core/InMemory/InMemoryQueryService.cs`
- [ ] T034 [P] [US4] Write unit tests for query evaluation (type filter, aspect presence, facet value contains/equals, AND/OR composition) in `test/Aster.Tests/InMemory/InMemoryQueryServiceTests.cs`

**Checkpoint**: User Story 4 fully implemented. Query-only path is independently testable.

---

## Phase 7: User Story 5 — Workbench Application (Priority: P3)

**Goal**: A standalone web application that hosts the Core SDK and In-Memory engine, providing a UI to visualize Definitions and Resource Instances for developer verification.

**User Scenario**: §3.6 — Developer runs `Aster.Web`, navigates to the dashboard, sees registered resource definitions and resource instances with their active versions.

**Independent Test**: Run `Aster.Web`, navigate to `/definitions` (or equivalent), confirm seeded definitions are visible; navigate to resource list, confirm seeded instances appear.

- [ ] T035 [US5] Register `Aster.Core` services in `src/apps/Aster.Web/Program.cs` via a DI extension method
- [ ] T036 [US5] Create `AsterCoreServiceCollectionExtensions.AddAsterCore()` in `src/core/Aster.Core/Extensions/AsterCoreServiceCollectionExtensions.cs` that registers `InMemoryResourceDefinitionStore`, `InMemoryResourceManager`, `InMemoryQueryService`, `SystemTextJsonAspectBinder`, `SystemTextJsonFacetBinder`, and `GuidIdentityGenerator` as `IIdentityGenerator`
- [ ] T037 [US5] Add a seed/demo data initializer (IHostedService) that registers a "Product" definition and a few sample resources on startup in `src/apps/Aster.Web/SeedDataInitializer.cs`
- [ ] T038 [P] [US5] Add a read-only `/api/definitions` endpoint (returns all registered definitions) in `src/apps/Aster.Web/Endpoints/DefinitionsEndpoints.cs`
- [ ] T039 [P] [US5] Add a read-only `/api/resources/{definitionId}` endpoint (returns all resource versions for a type) in `src/apps/Aster.Web/Endpoints/ResourcesEndpoints.cs`
- [ ] T040 [US5] Add a static `index.html` page with links to `/api/definitions` and `/api/resources/{definitionId}` (no Razor required) in `src/apps/Aster.Web/wwwroot/index.html`; confirm both endpoints return JSON in browser

**Checkpoint**: User Story 5 complete. Workbench app runs end-to-end with seeded data visible in UI.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Hardening, observability, and documentation across all stories.

- [ ] T041 [P] Add `ILogger<T>` log statements (Info/Warning/Error) to `InMemoryResourceManager` and `InMemoryResourceDefinitionStore` in respective files in `src/core/Aster.Core/InMemory/`
- [ ] T042 [P] Add XML doc comments (`///`) to all public interfaces and domain models per `docs/coding-conventions.md`
- [ ] T043 [P] Run quickstart.md validation: write an integration test or console app that reproduces the full quickstart flow in `test/Aster.Tests/Integration/QuickstartIntegrationTest.cs`
- [ ] T044 Add `README.md` usage section for `Aster.Core` SDK referencing quickstart.md in `src/core/Aster.Core/README.md`
- [ ] T045 Audit thread-safety: review all `ConcurrentDictionary` usage and activation lock logic; document any edge cases in `docs/architecture-review.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user stories**
- **Phase 3 (US1 — Definitions)**: Unblocks after Phase 2
- **Phase 4 (US2 — Versioning)**: Unblocks after Phase 2; shares in-memory store with US1 but independently testable
- **Phase 5 (US3 — Typed Aspects)**: Unblocks after Phase 2; decorates `ResourceVersion` (US2 output)
- **Phase 6 (US4 — Querying)**: Unblocks after Phase 2; reads from store built in US2
- **Phase 7 (US5 — Workbench)**: Unblocks after Phase 2; depends on US1/US3/US4 services being registered via DI
- **Phase 8 (Polish)**: Depends on all desired phases complete

### User Story Dependencies

| Story | Depends On | Notes |
|---|---|---|
| US1 (Definitions) | Phase 2 only | Fully independent |
| US2 (Versioning) | Phase 2 only | Independently testable once foundational models ready |
| US3 (Typed Aspects) | Phase 2 + US2 models | Extends `ResourceVersion` but can be tested standalone with stubs |
| US4 (Querying) | Phase 2 + US2 store | Uses same in-memory store; independently exercisable |
| US5 (Workbench) | All prior stories | Wireup/DI only — all logic already tested |

### Within Each User Story

- Models before services
- Services before endpoints/UI
- Tests can run in parallel with implementation within a phase (if team is split)
- Core implementation before integration

### Parallel Opportunities

- T005/T005a/T006, T007/T007a/T007b/T008/T009, T013/T013a (foundational models/contracts) — all independent files
- T014/T016 and T015 within US1
- T024/T025 within US2
- T028/T029 within US3
- T038/T039 within US5
- Entire US1, US2, US3, US4 can be developed concurrently once Phase 2 is done

---

## Parallel Example: User Story 2 (Versioning)

```bash
# Once Phase 2 is complete, launch all US2 tasks that are [P] together:
Task T024: "Unit tests for create/update/versioning in InMemoryResourceManagerTests.cs"
Task T025: "Unit tests for activation in InMemoryActivationTests.cs"

# Sequential tasks for US2:
T019 → T020 → T021 → T022 → T023
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (critical — blocks everything)
3. Complete Phase 3: US1 (definitions + fluent builder)
4. Complete Phase 4: US2 (create, version, activate)
5. **STOP and VALIDATE**: full create → update → activate flow tested
6. Ship as MVP

### Incremental Delivery

1. Setup + Foundational → solution compiles with contracts
2. + US1 → definition registry works ✅
3. + US2 → resource lifecycle works ✅ (MVP)
4. + US3 → typed POCO round-trip works ✅
5. + US4 → query abstraction works ✅
6. + US5 → workbench visualizes everything ✅
7. Polish pass

### Parallel Team Strategy

With 3+ developers (after Phase 2 completes):
- **Dev A**: US1 + US3 (definitions → typed aspects)
- **Dev B**: US2 (versioning + activation)
- **Dev C**: US4 (query AST + in-memory evaluator)
- **Dev D**: US5 (workbench wiring)

---

## Notes

- [P] tasks = different files, no shared state dependencies
- [Story] labels map tasks to user scenarios in `spec.md §2.x`
- `ConcurrentDictionary` is the concurrency primitive throughout (per `research.md`)
- `System.Text.Json` for aspect serialization (per `research.md`)
- `NSubstitute` for mocking in tests (per `research.md`)
- Multitarget: `net8.0;net9.0;net10.0` (per `plan.md`)
- Versions are immutable — `ResourceVersion` is a record, never mutated after creation
- Commit after each checkpoint to enable clean rollback per user story
