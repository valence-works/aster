# Getting Started

This guide walks through the full lifecycle of an Aster resource: define, create, update, activate, and query.

---

## Prerequisites

- .NET 8, 9, or 10
- `Aster.Core` package (multi-targeted: `net8.0`, `net9.0`, `net10.0`)

---

## 1. Register Services

In your `Program.cs` or startup code:

```csharp
using Aster.Core.Extensions;

builder.Services.AddAsterCore();
```

`AddAsterCore()` registers the following singletons:

| Interface | Implementation |
|---|---|
| `IResourceDefinitionStore` | `InMemoryResourceDefinitionStore` |
| `IResourceManager` | `InMemoryResourceManager` |
| `IResourceWriteStore` | `InMemoryResourceManager` |
| `IResourceQueryService` | `InMemoryQueryService` |
| `ITypedAspectBinder` | `SystemTextJsonAspectBinder` |
| `ITypedFacetBinder` | `SystemTextJsonFacetBinder` |
| `IIdentityGenerator` | `GuidIdentityGenerator` |

---

## 2. Define Typed Aspects

Aspects are plain C# classes or records. The property names become the facet keys.

```csharp
private record TitleAspect(string Title);
private record PriceAspect(decimal Amount, string Currency);
```

---

## 3. Register a Resource Definition

```csharp
using Aster.Core.Definitions;

var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithAspect<PriceAspect>()
    .Build();

await definitionStore.RegisterDefinitionAsync(definition);
```

The definition is immutable. Calling `RegisterDefinitionAsync` again with the same `DefinitionId` creates a **new version** — it never overwrites the existing one.

### Named aspects

To attach the same aspect type more than once, use a name discriminator:

```csharp
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Article")
    .WithAspect<TitleAspect>()
    .WithNamedAspect<TagsAspect>("Categories")
    .WithNamedAspect<TagsAspect>("Badges")
    .Build();
```

The attachment keys are then `"TagsAspect:Categories"` and `"TagsAspect:Badges"`.

### Singleton definitions

```csharp
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("SiteConfig")
    .WithSingleton()
    .WithAspect<SiteSettingsAspect>()
    .Build();
```

`CreateAsync` will throw `SingletonViolationException` if an instance already exists.

---

## 4. Create a Resource

```csharp
var resource = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new Dictionary<string, object>
    {
        ["TitleAspect"] = new TitleAspect("Super Gadget"),
        ["PriceAspect"] = new PriceAspect(99.99m, "USD"),
    }
});

// resource.Version == 1
// resource.ResourceId — stable across all future versions
// resource.Id        — unique ID for this exact version snapshot
// No activation entries → implicitly draft
```

To supply a stable, caller-controlled `ResourceId`:

```csharp
var resource = await manager.CreateAsync("Product", new CreateResourceRequest
{
    ResourceId = "product-001",
    InitialAspects = ...
});
```

If `"product-001"` already exists, `DuplicateResourceIdException` is thrown.

---

## 5. Update a Resource

`UpdateAsync` always produces a new immutable version. You must supply `BaseVersion` as an optimistic lock token.

```csharp
var v2 = await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
{
    BaseVersion = resource.Version, // must match current latest
    AspectUpdates = new Dictionary<string, object>
    {
        ["TitleAspect"] = new TitleAspect("Super Gadget Pro"),
        // PriceAspect not supplied — carried forward unchanged
    }
});

// v2.Version == 2
```

> `AspectUpdates` uses **State Replace** semantics — the entire aspect value is replaced for each key supplied. Keys not present in `AspectUpdates` are carried forward from the previous version unchanged.

If the resource was concurrently modified between your read and your update, `ConcurrencyException` is thrown.

---

## 6. Activate a Version

```csharp
await manager.ActivateAsync(
    resourceId: resource.ResourceId,
    version: 2,
    channel: "Published"
);
```

By default (`allowMultipleActive = false`) this deactivates any previously active version in `"Published"` first.

To allow multiple simultaneous active versions in the same channel:

```csharp
await manager.ActivateAsync(resource.ResourceId, version: 2, "Staging", allowMultipleActive: true);
```

---

## 7. Retrieve Versions

```csharp
// Latest version (highest version number)
var latest = await manager.GetLatestVersionAsync(resource.ResourceId);

// Specific version by number
var v1 = await manager.GetVersionAsync(resource.ResourceId, version: 1);

// All versions
var all = await manager.GetVersionsAsync(resource.ResourceId);

// All active versions in a channel
var active = await manager.GetActiveVersionsAsync(resource.ResourceId, channel: "Published");
```

---

## 8. Read Typed Aspects

```csharp
using Aster.Core.Extensions;

var title = latest!.GetAspect<TitleAspect>("TitleAspect", binder);
Console.WriteLine(title?.Title); // "Super Gadget Pro"

var price = latest!.GetAspect<PriceAspect>("PriceAspect", binder);
Console.WriteLine($"{price?.Amount} {price?.Currency}"); // "99.99 USD"
```

`GetAspect<T>` returns `default` if the aspect key is not present.

---

## 9. Full Example (Workbench / Seed Data)

The `Aster.Web` project seeds the following on startup as a reference:

```csharp
// Register definition
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithAspect<PriceAspect>()
    .Build();
await definitionStore.RegisterDefinitionAsync(definition);

// Create and immediately activate
var resource = await resourceManager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new Dictionary<string, object>
    {
        ["TitleAspect"] = new TitleAspect("Super Gadget"),
        ["PriceAspect"] = new PriceAspect(99.99m, "USD"),
    }
});
await resourceManager.ActivateAsync(resource.ResourceId, 1, "Published");
```

Then browse:

- `GET /api/definitions` — all registered definitions (latest version per ID)
- `GET /api/resources/{definitionId}` — all resource versions for that type

---

## Running the Workbench

```bash
cd src/apps/Aster.Web
dotnet run
```

Navigate to `http://localhost:5000` for the index page linking to both endpoints.

---

## Next Steps

- [Versioning & Activation](Versioning-and-Activation) — deep dive into the activation model
- [Typed Aspects & Facets](Typed-Aspects-and-Facets) — working with POCOs at the facet level
- [Querying](Querying) — filter resources by metadata and aspect/facet values

