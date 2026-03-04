# Architecture Overview

This page describes the internal architecture, layering, and key design decisions behind Aster.

---

## Guiding Principles

1. **Headless & backend-agnostic** — no coupling to any CMS, UI framework, or database.
2. **Immutable versions** — every change creates a new snapshot; nothing is ever mutated.
3. **Typed-first** — C# POCOs are first-class citizens from Phase 1; the raw dictionary storage is an internal detail.
4. **Portable query model** — the query AST is defined independently of any execution engine.
5. **Composable capabilities** — horizontal concerns (Tags, Owner, RBAC, …) are aspects, not hard-coded fields.

---

## Layered Design

```
+─────────────────────────────────────────────────+
|  Applications / Hosts (Aster.Web, your app)     |
+─────────────────────────────────────────────────+
|  Aster.Core (current)                           |
|  +──────────────+  +──────────────+             |
|  | Abstractions |  |  Extensions  |             |
|  | (interfaces) |  |  (DI, exts)  |             |
|  +──────+───────+  +──────────────+             |
|         |                                       |
|  +──────+───────+  +──────────────+             |
|  |   Models     |  |  Definitions |             |
|  | (domain)     |  |  (builder)   |             |
|  +──────────────+  +──────────────+             |
|         |                                       |
|  +──────+──────────────────────────────────+    |
|  |           InMemory (Phase 1)            |    |
|  |  ResourceManager · DefinitionStore      |    |
|  |  QueryService · ResourceStore           |    |
|  +─────────────────────────────────────────+    |
+─────────────────────────────────────────────────+
|  Aster.Persistence.<Backend>  (Phase 2+)        |
|  (PostgresJsonb / SqliteJson / Mongo / ...)      |
+─────────────────────────────────────────────────+
```

### Layer responsibilities

| Layer | Package (current) | Responsibility |
|---|---|---|
| **Abstractions** | `Aster.Core` | Interfaces: `IResourceManager`, `IResourceDefinitionStore`, `IResourceQueryService`, `ITypedAspectBinder`, `ITypedFacetBinder`, `IIdentityGenerator`, `IResourceWriteStore` |
| **Models** | `Aster.Core` | Immutable domain records: `Resource`, `ResourceDefinition`, `AspectDefinition`, `FacetDefinition`, `AspectInstance`, `FacetValue`, `ActivationState`, `ResourceQuery`, filter types |
| **Definitions** | `Aster.Core` | `ResourceDefinitionBuilder` — fluent code-first API |
| **Services** | `Aster.Core` | `SystemTextJsonAspectBinder`, `SystemTextJsonFacetBinder`, `GuidIdentityGenerator` |
| **InMemory** | `Aster.Core` | Phase 1 implementations: `InMemoryResourceManager`, `InMemoryResourceDefinitionStore`, `InMemoryResourceStore`, `InMemoryQueryService` |
| **Extensions** | `Aster.Core` | `AddAsterCore()` DI extension; `GetAspect<T>`, `SetAspect<T>`, `GetFacet<T>`, `SetFacet<T>` |

---

## Domain Model

### Definitions vs Instances

```
ResourceDefinition  --- (contains) --> AspectDefinition[]
                                              |
                                       (contains) --> FacetDefinition[]

Resource            --- (references) --> ResourceDefinition (by DefinitionId)
                    --- (contains)  --> AspectInstance[]
                                              |
                                       (contains) --> FacetValue[]
```

### Universal Versioning Pattern

Every entity type follows the same identity scheme:

| Property | Meaning |
|---|---|
| `<Entity>Id` | Logical persistent identifier (stable across versions) |
| `Id` | Version snapshot unique identifier (GUID) |
| `Version` | Ordinal (1, 2, 3…) |

Applied to: `ResourceDefinition`, `AspectDefinition`, `FacetDefinition`, `Resource`.

### Aspect Keys

| Attachment type | Key format | Example |
|---|---|---|
| Unnamed | `AspectDefinitionId` | `"TitleAspect"` |
| Named | `"{AspectDefinitionId}:{Name}"` | `"TagsAspect:Categories"` |

---

## Activation Model

Activation is a **separate concern** from versioning:

- A `Resource` carries no explicit status field.
- `ActivationState` records associate `(ResourceId, Version)` pairs with channels.
- A version with zero activation records is implicitly **draft**.
- A version with one or more active records is **active** in those channels.

### Channel semantics

- Channel names are arbitrary strings (case-sensitive).
- `allowMultipleActive = false` (default): single-active per channel — activating V2 deactivates others.
- `allowMultipleActive = true`: multi-active — V2 is added alongside existing active versions.

---

## Query Architecture

The `ResourceQuery` AST is the portable query surface:

```
ResourceQuery
  DefinitionId?              (shorthand definition filter)
  Filter: FilterExpression?
    MetadataFilter           (top-level field equality/contains)
    AspectPresenceFilter     (has aspect attached?)
    FacetValueFilter         (aspect facet value comparison)
    LogicalExpression        (And / Or / Not combinator)
  Skip?
  Take?
```

The AST is evaluated by `IResourceQueryService`. Phase 1 translates to LINQ. Future backends will translate to SQL/JSONB/MongoDB aggregations.

**Design decision:** `IQueryable<T>` was deliberately avoided — it is fine within a single ORM but breaks across provider boundaries.

---

## Planned Package Split (Phase 2+)

```
Aster.Abstractions       -- interfaces only
Aster.Definitions        -- builder API
Aster.Runtime            -- services, lifecycle, versioning pipelines
Aster.Querying           -- ResourceQuery AST + IResourceQueryService
Aster.Indexing           -- index field model, IQueryCapabilities, query planner
Aster.Persistence.X      -- provider: SqliteJson, PostgresJsonb, Mongo, ...
Aster.Hosting            -- AddAsterCore() and DI glue
Aster.Recipes            -- optional: export/import recipe execution
```

---

## Key Design Decisions

### 1. Activation channels over Draft/Published binary

Named activation channels rather than a binary `Draft | Published` state. Supports complex delivery scenarios (`Preview`, `A/B`, `Mobile-Specific`, `Staging`) without schema changes.

### 2. Immutable append-only versions

Every `UpdateAsync` appends a new version; no in-place mutation. Simplifies optimistic concurrency, enables auditing, supports natural time-travel queries.

### 3. Typed aspects from Phase 1

C# POCOs are supported from Phase 1, not deferred. Deferring would mean building stringly-typed first and bolting on types later — leading to awkward mapping layers.

### 4. Query contract defined before persistence

The `ResourceQuery` AST was defined in Phase 1 before any persistence backend. Storage shapes are dictated by access patterns; building persistence without a query contract risks a storage rewrite.

### 5. No `IQueryable<T>` as a public abstraction

`IQueryable` breaks across provider boundaries. A custom AST gives explicit control over supported operators and semantics.

---

## Related

- [Roadmap](Roadmap) — phase-by-phase evolution
- [Querying](Querying) — query AST details
- [DI Registration](DI-Registration) — service wiring
