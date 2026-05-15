# Aster.Core

The core SDK for the Aster versioned resource platform.  
Provides **definitions**, **versioned resources**, **activation channels**, **typed aspects**, provider-backed lifecycle orchestration, and default in-memory querying — all wired with standard `Microsoft.Extensions.DependencyInjection`.

---

## Installation

```bash
dotnet add package Aster.Core
```

Or add a project reference:

```xml
<ProjectReference Include="../core/Aster.Core/Aster.Core.csproj" />
```

---

## Quick-start

> Full annotated example: [`specs/001-core-sdk-foundation/quickstart.md`](../../../specs/001-core-sdk-foundation/quickstart.md)

### 1. Register services

```csharp
// Program.cs or wherever you configure DI
builder.Services.AddAsterCore();
```

`AddAsterCore()` registers the core singleton services:

| Service | Interface |
|---|---|
| `InMemoryResourceDefinitionStore` | `IResourceDefinitionStore` |
| `InMemoryResourceStore` | `IResourceVersionReader`, `IResourceVersionWriter` |
| `DefaultResourceManager` | `IResourceManager` |
| `InMemoryQueryService` | `IResourceQueryService` |
| `GuidIdentityGenerator` | `IIdentityGenerator` |
| `SystemTextJsonAspectBinder` | `ITypedAspectBinder` |
| `SystemTextJsonFacetBinder` | `ITypedFacetBinder` |

---

### 2. Define a resource type

```csharp
var builder = new ResourceDefinitionBuilder();
var definition = builder.WithDefinitionId("Product")
       .WithAspect<TitleAspect>()
       .WithAspect<PriceAspect>()
       .Build();

var definitionStore = serviceProvider.GetRequiredService<IResourceDefinitionStore>();
await definitionStore.RegisterDefinitionAsync(definition);
```

---

### 3. Create a resource

```csharp
var manager = serviceProvider.GetRequiredService<IResourceManager>();

var resource = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new()
    {
        { "Title", new TitleAspect("Super Gadget") },
        { "Price", new PriceAspect(99.99m, "USD") }
    }
});

// resource.ResourceId  → logical ID (shared across all versions)
// resource.Id          → version-specific GUID
// resource.Version     → 1
```

---

### 4. Update a resource (creates a new version)

```csharp
var latest = await manager.GetLatestVersionAsync(resource.ResourceId);

var v2 = await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
{
    BaseVersion = latest.Version,   // optimistic concurrency token
    AspectUpdates = new()
    {
        { "Title", new TitleAspect("Super Gadget Pro") }
    }
});

// v2.Version     → 2
// v2.ResourceId  → same as resource.ResourceId
// v2.Id          → new GUID
```

---

### 5. Activate a version in a channel

```csharp
await manager.ActivateAsync(resource.ResourceId, v2.Version, "Published");

var active = await manager.GetActiveVersionsAsync(resource.ResourceId, "Published");
// active.Single().Version == 2
```

---

### 6. Query resources

```csharp
var queryService = serviceProvider.GetRequiredService<IResourceQueryService>();

var results = await queryService.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    Filter = new FacetValueFilter("Title", "Title", "Super Gadget Pro", ComparisonOperator.Equals),
    Take = 10,
});
```

---

## Architecture overview

```
IResourceDefinitionStore      — stores versioned ResourceDefinition schemas
IResourceManager              — provider-backed create / update / activate orchestration
IResourceVersionWriter           — low-level version/activation persistence hook
IResourceVersionReader           — low-level read hook for query candidate version sets
IResourceQueryService         — portable query service; default is LINQ-based in-memory
ITypedAspectBinder            — serialise/deserialise full aspects (System.Text.Json)
ITypedFacetBinder             — serialise/deserialise individual facet values
IIdentityGenerator            — pluggable ID strategy (default: Guid)
```

Key invariants:

- `Resource` is **immutable** — every `UpdateAsync` call produces a **new** version snapshot. The original version is never mutated.
- `ResourceId` is the **logical** identifier shared across all versions. Each version has its own `Id` (GUID).
- Activation channels are independent: a resource can be active in `"Published"` at V2 and in `"Staging"` at V3 simultaneously.
- Optimistic concurrency is enforced on `UpdateAsync` (must supply `BaseVersion == current latest Version`) and `ActivateAsync` (must supply the current latest version number).

---

## Thread safety

All in-memory implementations are thread-safe:

- `ConcurrentDictionary<K,V>` for key-level bucket operations.
- `lock(list)` guards all `List<T>` mutations (version lists, definition version lists).
- `lock(channelActivations)` guards `HashSet<int>` activation sets.

---

## Extending Aster.Core

Swap out any single service by re-registering after `AddAsterCore()`:

```csharp
services.AddAsterCore();
// Override identity generation
services.AddSingleton<IIdentityGenerator, SequentialIdGenerator>();
```

---

## Targets

`net8.0` · `net9.0` · `net10.0`
