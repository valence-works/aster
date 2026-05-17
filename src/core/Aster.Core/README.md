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
| `InMemoryQueryCapabilitiesProvider` | `IResourceQueryCapabilitiesProvider` |
| `ResourceQueryValidator` | `IResourceQueryValidator` |
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
var query = new ResourceQuery
{
    DefinitionId = "Product",
    Filter = new FacetValueFilter("Title", "Title", "Super Gadget Pro", ComparisonOperator.Equals),
    Take = 10,
};

var results = await queryService.QueryAsync(query);
```

Preflight the query against the active provider:

```csharp
var validator = serviceProvider.GetRequiredService<IResourceQueryValidator>();
var validation = validator.Validate(query);

if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code} ({failure.Feature}): {failure.Message}");
}
```

Provider capabilities are matched to the active query provider by explicit provider key. If a custom provider has no matching capability declaration, validation fails closed with `capabilities-not-declared`. Query execution still enforces unsupported shapes and throws `UnsupportedQueryFeatureException` with stable `Code`, `Feature`, optional `Path`, and an actionable message.

Custom query providers can be registered with the provider-authoring helper:

```csharp
services
    .AddAsterCore()
    .AddResourceQueryProvider<MyQueryService, MyQueryCapabilitiesProvider>();
```

The helper registers the provider concrete types and shared query/provider interfaces as singletons, keeping the active query service, provider identity, and capability declaration together without introducing provider discovery or a registry. Hosts that need different lifetimes can still use explicit manual DI registration.

Or build common typed aspect filters and sorts without repeating convention-based identifiers:

```csharp
var title = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .StartsWith("Gadget");

var titleSet = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .In("Gadget Pro", "Gadget Plus");

var price = TypedQuery.For<PriceAspect>()
    .Facet(x => x.Amount)
    .Range(max: 100m);

var query = new ResourceQuery
{
    Filter = TypedQuery.And(title, titleSet, price),
    Sorts =
    [
        TypedQuery.For<PriceAspect>()
            .Facet(x => x.Amount)
            .Descending(),
    ],
};
```

---

## Architecture overview

```
IResourceDefinitionStore      — stores versioned ResourceDefinition schemas
IResourceManager              — provider-backed create / update / activate orchestration
IResourceVersionWriter           — low-level version/activation persistence hook
IResourceVersionReader           — low-level read hook for query candidate version sets
IResourceQueryService         — portable query service; default is LINQ-based in-memory
IResourceQueryCapabilitiesProvider — declares active provider query support
IResourceQueryValidator        — preflights ResourceQuery against provider capabilities
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
