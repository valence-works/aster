# Aster

![ster](./branding/banner2.jpg)

**Aster** is a .NET SDK for defining, versioning, and querying composable resources using a **Resource → Aspect → Facet** model.

It provides a headless, backend-agnostic foundation for attaching reusable, cross-cutting capabilities (Tags, Owner, RBAC, Scheduling, …) to any resource type — without hard-coding every entity from scratch.

> **Status:** Phase 5 starting — Core SDK, in-memory engine, SQLite JSON persistence/querying, query capability discovery, preflight validation, typed query helpers, provider conformance support, portable operators, SQLite date-like ranges, explicit index projection contracts, definition schema upgrades, portability primitives, host lifecycle hooks, and explicit tenant scoping are available. See [Roadmap](#roadmap) for future phases.

---

## Table of Contents

- [Concepts](#concepts)
- [Quick Start](#quick-start)
- [DI Registration](#di-registration)
- [Querying](#querying)
- [Versioning & Activation](#versioning--activation)
- [Typed Aspects & Facets](#typed-aspects--facets)
- [Project Structure](#project-structure)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## Concepts

| Term | Description |
|---|---|
| **Resource Definition** | The schema for a resource type (e.g. `Product`, `WorkflowDefinition`). Immutable — each update appends a new definition version. |
| **Resource** | A versioned instance of a Resource Definition. `ResourceId` is stable across versions; `Id` identifies the exact version snapshot. |
| **Aspect Definition** | A reusable "part" that can be attached to any Resource Definition (e.g. `TitleAspect`, `PriceAspect`). |
| **Aspect Instance** | The per-resource-version data for an attached aspect, stored as a dictionary of facet values. |
| **Facet Definition** | A typed field declared inside an Aspect Definition (e.g. `Title: string`, `Amount: decimal`). |
| **Facet Value** | The actual value of a facet on an aspect instance. |
| **Activation Channel** | A named delivery context (e.g. `"Published"`, `"Staging"`). A resource version becomes _active_ when placed in a channel. Multiple channels and multiple simultaneously active versions are supported. |
| **Tenant Scope** | An explicit opaque tenant boundary for definitions, resources, activation state, queries, schema upgrades, portability, and lifecycle hooks. Omitted scope resolves to the default single-tenant scope. |

Resources follow an **append-only versioning model** — versions are never mutated. A version with no activation entry is implicitly a draft.

---

## Quick Start

### 1. Register services (ASP.NET Core / Generic Host)

```csharp
builder.Services.AddAsterCore();
```

This registers the in-memory store, resource manager, query service, typed aspect/facet binders, and identity generator.

For SQLite-backed persistence and querying, call `AddAsterSqliteJson(...)` after `AddAsterCore()`:

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});
```

### 2. Define a Resource Type

```csharp
using Aster.Core.Definitions;

// Plain C# records become typed aspects
private record TitleAspect(string Title);
private record PriceAspect(decimal Amount, string Currency);

var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithAspect<PriceAspect>()
    .Build();

await definitionStore.RegisterDefinitionAsync(definition);
```

For tenant-aware hosts, pass a `TenantScope` explicitly:

```csharp
var tenant = TenantScope.FromTenantId("tenant-a");
await definitionStore.RegisterDefinitionAsync(definition, tenant);
```

### 3. Create a Resource

```csharp
var resource = await manager.CreateAsync("Product", new CreateResourceRequest
{
    TenantScope = tenant, // omit for default single-tenant behavior
    InitialAspects = new Dictionary<string, object>
    {
        ["TitleAspect"] = new TitleAspect("Super Gadget"),
        ["PriceAspect"] = new PriceAspect(99.99m, "USD"),
    }
});
// resource.Version == 1, no activation entry → implicitly draft
```

### 4. Update (Save a New Version)

```csharp
var v2 = await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
{
    BaseVersion = resource.Version,   // optimistic lock
    AspectUpdates = new Dictionary<string, object>
    {
        ["TitleAspect"] = new TitleAspect("Super Gadget Pro"),
    }
});
// v2.Version == 2
```

### 5. Activate in a Channel

```csharp
await manager.ActivateAsync(resource.ResourceId, version: 2, channel: "Published");
```

### 6. Read Back with Typed Aspects

```csharp
var latest = await manager.GetLatestVersionAsync(resource.ResourceId);

var title = latest!.GetAspect<TitleAspect>("TitleAspect", binder);
Console.WriteLine(title?.Title); // "Super Gadget Pro"
```

---

## DI Registration

`AddAsterCore()` wires the following services:

| Interface | Default Implementation |
|---|---|
| `IResourceDefinitionStore` | `InMemoryResourceDefinitionStore` |
| `IResourceManager` | `DefaultResourceManager` |
| `IResourceVersionWriter` | `InMemoryResourceStore` |
| `IResourceVersionReader` | `InMemoryResourceStore` |
| `IResourceQueryService` | `InMemoryQueryService` |
| `IResourceQueryCapabilitiesProvider` | `InMemoryQueryCapabilitiesProvider` |
| `IResourceQueryValidator` | `ResourceQueryValidator` |
| `IResourceSchemaVersionService` | `ResourceSchemaVersionService` |
| `ITypedAspectBinder` | `SystemTextJsonAspectBinder` |
| `ITypedFacetBinder` | `SystemTextJsonFacetBinder` |
| `IIdentityGenerator` | `GuidIdentityGenerator` |

All services are registered as **singletons** — the in-memory store is the single shared instance within the process.

---

## Querying

Use `IResourceQueryService` with a portable `ResourceQuery` AST:

```csharp
var query = new ResourceQuery
{
    TenantScope = tenant,
    DefinitionId = "Product",
    Filter = new FacetValueFilter("TitleAspect", "Title", "Gadget", ComparisonOperator.Contains),
    Sorts = [new SortExpression("Created", SortDirection.Descending)],
    Skip = 0,
    Take = 20,
};

var results = await queryService.QueryAsync(query);
```

The in-memory evaluator supports `Equals`, `Contains`, and `Range`, plus latest/all/active/draft version scopes.

The SQLite JSON provider executes the same `ResourceQuery` AST in SQLite for its supported subset: metadata filters/sorts, version scopes, paging, aspect presence, scalar facet `Equals`/`Contains`, and numeric facet `Range`. Unsupported provider query shapes throw `UnsupportedQueryFeatureException`.

Inspect provider support and validate before execution:

```csharp
var capabilities = serviceProvider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;
var validation = serviceProvider.GetRequiredService<IResourceQueryValidator>().Validate(query);
```

Typed helpers build the same portable query model without repeating common aspect/facet strings:

```csharp
var filter = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .Contains("Gadget");
```

---

## Versioning & Activation

- **Immutable versions** — every call to `UpdateAsync` appends a new version.
- **Definition lineage** — every new resource records the active `ResourceDefinition.Version` in `Resource.DefinitionVersion`. Normal updates preserve that recorded lineage.
- **Optimistic concurrency** — `UpdateResourceRequest.BaseVersion` must match the current latest; mismatches throw `ConcurrencyException`.
- **Activation** — `ActivateAsync(resourceId, version, channel, allowMultipleActive)`:
  - `allowMultipleActive = false` (default): deactivates all other versions in the channel first.
  - `allowMultipleActive = true`: adds alongside existing active versions.
- **Retrieval helpers** on `IResourceManager`:
  - `GetLatestVersionAsync` — the most recently created version.
  - `GetVersionAsync(resourceId, version)` — a specific version snapshot.
  - `GetVersionsAsync(resourceId)` — all versions.
  - `GetActiveVersionsAsync(resourceId, channel)` — all active versions in a channel.

Use `IResourceSchemaVersionService` when a host needs to inspect or explicitly advance definition lineage:

```csharp
var schemaVersions = serviceProvider.GetRequiredService<IResourceSchemaVersionService>();
var status = await schemaVersions.GetSchemaStatusAsync(resource);

var upgrade = await schemaVersions.UpgradeAsync(resource.ResourceId, new ResourceSchemaUpgradeRequest
{
    BaseVersion = resource.Version,
    TargetDefinitionVersion = status.LatestDefinitionVersion,
});
```

Schema status is evaluated for one resource version at a time. Explicit upgrades append a new resource version, default to the latest definition version when no target is supplied, preserve existing aspect data by default, and report `CarriedForwardAspectKeys` for aspect keys not declared by the target definition. A target equal to the source lineage returns `ResourceSchemaUpgradeStatus.NoOp`; invalid targets throw `ResourceSchemaUpgradeException` with a stable `Code`.

### Exception reference

| Exception | When thrown |
|---|---|
| `ConcurrencyException` | `BaseVersion` mismatch on update or activate |
| `VersionNotFoundException` | Requested version does not exist |
| `ResourceSchemaUpgradeException` | Explicit schema upgrade target is invalid or unavailable |
| `SingletonViolationException` | Creating a second instance of a singleton definition |
| `DuplicateResourceIdException` | Caller-supplied `ResourceId` already exists |
| `DuplicateAspectAttachmentException` | Same aspect key attached twice to a definition |

---

## Typed Aspects & Facets

Any C# class or record can be used as a typed aspect:

```csharp
record PriceAspect(decimal Amount, string Currency);
```

**Read** a typed aspect from a resource:

```csharp
var price = resource.GetAspect<PriceAspect>("PriceAspect", binder);
```

**Write** a typed aspect (returns a new immutable `Resource` record — State Replace semantics):

```csharp
var updated = resource.SetAspect("PriceAspect", new PriceAspect(129.99m, "USD"), binder);
```

The same pattern works at the **facet level** via `AspectInstance.GetFacet<T>` and `AspectInstance.SetFacet<T>`.

The default `ITypedAspectBinder` implementation uses `System.Text.Json`.

---

## Project Structure

```
src/
  core/
    Aster.Core/              ← Main SDK library (net8.0 / net9.0 / net10.0)
      Abstractions/          ← Interfaces (IResourceManager, IResourceDefinitionStore, …)
      Definitions/           ← ResourceDefinitionBuilder (fluent API)
      Exceptions/            ← Typed exceptions
      Extensions/            ← DI helpers, GetAspect / SetAspect extensions
      InMemory/              ← In-memory implementations
      Models/                ← Domain models (Resource, AspectDefinition, ResourceQuery, …)
      Services/              ← SystemTextJson binders, GuidIdentityGenerator
  persistence/
    Aster.Persistence.SqliteJson/
      ← SQLite JSON definition/resource version/query provider
  apps/
    Aster.Web/               ← Workbench / playground (ASP.NET Core minimal API)
test/
  Aster.Tests/               ← xUnit tests (unit + integration)
docs/                        ← Architecture review, coding conventions, roadmap
specs/                       ← Feature specs (001-core-sdk-foundation, …)
```

---

## Roadmap

| Phase | Title | Status |
|---|---|---|
| **1** | Core SDK & In-Memory Engine | ✅ Complete |
| **2A** | SQLite JSON Persistence & Querying | ✅ Complete |
| **3** | Query Capabilities & Typed Querying | ✅ Complete |
| **4** | Portability & Integration Hooks | 🚧 In Progress |
| **5** | Multi-tenancy, Policies, Advanced Versioning | 🚧 In Progress |
| **6** | Operational Hardening (concurrency, perf, migrations) | 📋 Planned |

See [`docs/Roadmap.md`](docs/Roadmap.md) and [`wiki/Roadmap.md`](wiki/Roadmap.md) for the full phase breakdown with epics and definitions of done.

### Planned package layout (future)

```
Aster.Abstractions
Aster.Definitions
Aster.Runtime
Aster.Querying
Aster.Indexing
Aster.Persistence.<Backend>   (e.g., SqliteJson, PostgresJsonb, Mongo)
Aster.Hosting
Aster.Recipes                 (optional)
```

---

## Contributing

Please read [`docs/coding-conventions.md`](docs/coding-conventions.md) before submitting a PR.

---

## License

MIT — see [`LICENSE`](LICENSE).
