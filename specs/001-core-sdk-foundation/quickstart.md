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
builder.WithId("Product")
       .WithAspect<TitleAspect>()
       .WithAspect<PriceAspect>()
       .Build();

var definition = builder.Result;
await services.GetRequiredService<IResourceDefinitionStore>().RegisterAsync(definition);
```

### 2. Create a Resource

```csharp
var manager = services.GetRequiredService<IResourceManager>();

var version = await manager.CreateAsync("Product", new CreateResourceRequest {
    InitialAspects = new() {
        { "Title", new TitleAspect("Super Gadget") },
        { "Price", new PriceAspect(99.99m, "USD") }
    }
});

var id = version.ResourceId;
```

### 3. Update & Version

```csharp
// Get latest version for optimistic lock
var latest = await manager.GetLatestVersionAsync(id);

// Update title
var newVersion = await manager.UpdateAsync(id, new UpdateResourceRequest {
    BaseVersion = latest.Version,
    AspectUpdates = new() {
        { "Title", new TitleAspect("Super Gadget Pro") }
    }
});
// Result: Version 2 created. version 1 remains unchanged.
```

### 4. Activate

```csharp
// Publish Version 2
await manager.ActivateAsync(id, 2, "Published");

// Verify active version
var active = await manager.GetActiveVersionsAsync(id, "Published");
// active.Single().Version should be 2
```
