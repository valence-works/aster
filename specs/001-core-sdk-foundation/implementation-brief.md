# Implementation Brief: `001-core-sdk-foundation`

**Repo**: `/Users/sipke/Projects/ValenceWorks/aster/main`  
**Branch**: `001-core-sdk-foundation`  
**All spec artifacts**: `specs/001-core-sdk-foundation/` — read `spec.md`, `plan.md`, `data-model.md`, `contracts/Abstractions.cs`, `quickstart.md`, `tasks.md` before writing any code.  
**Coding conventions**: `docs/coding-conventions.md` — mandatory.  
**Task list**: `specs/001-core-sdk-foundation/tasks.md` — 52 tasks T001–T045 (with sub-tasks T005a, T007a/b, T008, T012a, T013a, T026a, T028a). Work through them in phase order; mark each `[x]` when done.

---

## What to build

A C# SDK library `Aster.Core` (`src/core/Aster.Core/`) targeting `net8.0;net9.0;net10.0`, plus wiring into the existing `src/apps/Aster.Web` and `test/Aster.Tests` projects.

---

## Critical architecture decisions (do not deviate)

### Universal Versioning Pattern

Every versioned model has two IDs:

| Model | Logical ID (persistent) | Version-specific `Id` (GUID) |
|---|---|---|
| `ResourceDefinition` | `DefinitionId` | `Id` |
| `AspectDefinition` | `AspectDefinitionId` | `Id` |
| `Resource` | `ResourceId` | `Id` |

> `FacetDefinition` is a simple field descriptor (analogous to a Field on a Part in Orchard Core). It carries only `FacetDefinitionId`, `Type`, and `IsRequired` — no independent `Id` or `Version`.

### `Resource` IS a version snapshot

There is no separate `ResourceVersion` type. A new version = a new `Resource` record with the same `ResourceId`, new `Id` (GUID), incremented `Version`. Records are immutable.

### `AspectDefinition` / `FacetDefinition` are embedded snapshots

Embedded inside `ResourceDefinition` — not independently stored. No `IAspectDefinitionStore` needed in Phase 1. `FacetDefinition` is a simple field descriptor (no `Id` or `Version`); it inherits its version context from its parent `AspectDefinition`.

### Definition versioning

`RegisterDefinitionAsync` always appends a new version (auto-increments `Version`); never overwrites. Internal store key = `DefinitionId`.

### Status is derived

No `Status` field on `Resource`. A version absent from all `ActivationState.ActiveVersions` is implicitly *draft*; presence in a channel's `ActiveVersions` = *active*.

### Identity generation

`IIdentityGenerator` service (`GuidIdentityGenerator` default). `CreateResourceRequest.ResourceId` is optional (caller-supplied logical ID); if null, engine calls `IIdentityGenerator.NewId()`. Throw `DuplicateResourceIdException` if supplied ID already exists.

### Singleton enforcement

If `ResourceDefinition.IsSingleton == true` and any instance for `DefinitionId` already exists, `CreateAsync` throws `SingletonViolationException`.

### Activation concurrency

Optimistic lock token = current latest `Resource.Version`. Throw `ConcurrencyException` if it changed since caller last read.

### Query AST scope

`Range` comparator is defined in the AST but `InMemoryQueryService` MUST throw `NotSupportedException` for Range. Phase 1 evaluator supports Equals + Contains only (spec §6).

### `IResourceWriteStore`

Constitution Principle V requires this. `InMemoryResourceManager` implements it internally; the interface must exist as a standalone contract. Methods: `SaveVersionAsync(Resource)`, `UpdateActivationAsync(...)`.

---

## Key internal storage structures (all `InMemory/`)

```csharp
// Definitions
ConcurrentDictionary<string, List<ResourceDefinition>>   // key = DefinitionId

// Resource versions
ConcurrentDictionary<string, List<Resource>>             // key = ResourceId

// Activations
ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<int>>>
// key = ResourceId → channel → set of active Version numbers
```

---

## Contracts (already written — transcribe, do not invent)

`specs/001-core-sdk-foundation/contracts/Abstractions.cs` contains the final signed interfaces:

- `IResourceDefinitionStore` — `GetDefinitionAsync`, `GetDefinitionVersionAsync`, `RegisterDefinitionAsync`, `ListDefinitionsAsync`
- `IResourceManager` — `CreateAsync`, `UpdateAsync`, `GetVersionAsync`, `GetVersionsAsync`, `GetLatestVersionAsync`, `ActivateAsync`, `DeactivateAsync`, `GetActiveVersionsAsync`
- `IIdentityGenerator`
- `CreateResourceRequest` (`ResourceId?`, `InitialAspects`)
- `UpdateResourceRequest` (`BaseVersion`, `AspectUpdates`)
- `IResourceWriteStore` — `SaveVersionAsync`, `UpdateActivationAsync`

---

## Exceptions to define

All in `src/core/Aster.Core/Exceptions/AsterExceptions.cs`:

`VersionNotFoundException`, `ConcurrencyException`, `DuplicateAspectAttachmentException`, `DuplicateResourceIdException`, `SingletonViolationException`

---

## Typed Aspects + Typed Facets (Phase 5, US3)

- `ITypedAspectBinder` → `SystemTextJsonAspectBinder` — full aspect POCO, State Replace semantics
- `ITypedFacetBinder` → `SystemTextJsonFacetBinder` — single facet value, State Replace semantics
- Extension methods: `Resource.GetAspect<T>()` / `SetAspect<T>()` in `ResourceExtensions.cs`
- Extension methods: `AspectInstance.GetFacet<T>()` / `SetFacet<T>()` in `AspectInstanceExtensions.cs`
- Builder: `.WithTypedAspect<T>()` and `.WithTypedFacet<T>()` on `ResourceDefinitionBuilder`
- Serializer: `System.Text.Json` (not Newtonsoft)

---

## Workbench (Phase 7, US5)

Wire `Aster.Core` into existing `src/apps/Aster.Web`:

- `AddAsterCore()` DI extension — registers all in-memory services + binders + `GuidIdentityGenerator`
- `SeedDataInitializer` (`IHostedService`) — registers a "Product" definition + sample resources on startup
- `GET /api/definitions` — returns latest definitions as JSON (read-only)
- `GET /api/resources/{definitionId}` — returns all resource versions as JSON (read-only)
- `wwwroot/index.html` — static page linking both endpoints; no Razor, no forms, no mutation UI

---

## Tests

- Framework: xUnit + NSubstitute
- Project: `test/Aster.Tests/` (targets `net9.0`)
- Write tests as part of each task; `[P]`-marked tests within a phase can run in parallel with implementation
- Exact test file paths are specified per task in `tasks.md`

---

## Project structure to create

```
src/core/Aster.Core/
├── Abstractions/
│   ├── IResourceDefinitionStore.cs
│   ├── IResourceManager.cs
│   ├── IIdentityGenerator.cs
│   ├── IResourceWriteStore.cs
│   ├── ITypedAspectBinder.cs
│   ├── ITypedFacetBinder.cs
│   ├── IResourceQueryService.cs
│   └── Requests.cs
├── Definitions/
│   └── ResourceDefinitionBuilder.cs
├── Exceptions/
│   └── AsterExceptions.cs
├── Extensions/
│   ├── AsterCoreServiceCollectionExtensions.cs
│   ├── ResourceExtensions.cs
│   └── AspectInstanceExtensions.cs
├── InMemory/
│   ├── InMemoryResourceDefinitionStore.cs
│   ├── InMemoryResourceStore.cs
│   ├── InMemoryResourceManager.cs
│   └── InMemoryQueryService.cs
├── Models/
│   ├── Definitions/
│   │   ├── ResourceDefinition.cs
│   │   ├── AspectDefinition.cs
│   │   └── FacetDefinition.cs
│   ├── Instances/
│   │   ├── Resource.cs
│   │   ├── ActivationState.cs
│   │   ├── AspectInstance.cs
│   │   └── FacetValue.cs
│   └── Querying/
│       ├── ResourceQuery.cs
│       ├── FilterExpression.cs
│       ├── LogicalExpression.cs
│       └── ComparisonOperator.cs
└── Services/
    ├── GuidIdentityGenerator.cs
    ├── SystemTextJsonAspectBinder.cs
    └── SystemTextJsonFacetBinder.cs
```
