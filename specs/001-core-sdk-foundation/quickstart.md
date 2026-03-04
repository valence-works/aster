# Quickstart: Aster Core SDK Phase 1

## Prerequisites
- .NET 10.0 SDK (supports multitargeting .NET 8/9/10)
- C# IDE (VS Code / Rider / Visual Studio)

## Installation

Add the `Aster.Core` reference to your project.

```bash
dotnet add package Aster.Core
```

## Basic Usage

### 1. Define a Resource Type

```csharp
var builder = new ResourceDefinitionBuilder();
var definition = builder.WithDefinitionId("Product")
       .WithAspect<TitleAspect>()
       .WithAspect<PriceAspect>()
       .Build();

await services.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
```

### 2. Create a Resource

```csharp
var manager = services.GetRequiredService<IResourceManager>();

// resource is a Resource (version snapshot); ResourceId is the logical persistent ID
var resource = await manager.CreateAsync("Product", new CreateResourceRequest {
    InitialAspects = new() {
        { "Title", new TitleAspect("Super Gadget") },
        { "Price", new PriceAspect(99.99m, "USD") }
    }
});

var resourceId = resource.ResourceId; // logical ID, shared across all versions
```

### 3. Update & Version

```csharp
// Get latest version for optimistic lock
var latest = await manager.GetLatestVersionAsync(resourceId);

// Update title — produces a new Resource (V2) with the same ResourceId
var v2 = await manager.UpdateAsync(resourceId, new UpdateResourceRequest {
    BaseVersion = latest.Version,
    AspectUpdates = new() {
        { "Title", new TitleAspect("Super Gadget Pro") }
    }
});
// Result: Resource V2 created (new Id, Version=2, same ResourceId). V1 remains unchanged.
```

### 4. Activate

```csharp
// Publish Version 2
await manager.ActivateAsync(resourceId, 2, "Published");

// Verify active version
var active = await manager.GetActiveVersionsAsync(resourceId, "Published");
// active.Single().Version should be 2
```
