# Aster.Core

Core SDK for [Aster](https://github.com/valence-works/aster): define resource types at runtime or in code as compositions of reusable aspects and typed facets, then create immutable resource versions, activate them per channel (for example `staging` and `production`), and query them through a portable query model.

Aster is a control-plane store for authored, versioned data such as definitions, configuration, templates and user-defined entities. It is not a store for runtime or high-throughput data.

```csharp
services.AddAsterCore();

var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .Build();
await definitionStore.RegisterDefinitionAsync(definition);

var product = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new() { ["TitleAspect"] = new TitleAspect("Super Gadget") },
});
await manager.ActivateAsync(product.ResourceId, product.Version, "Published");

record TitleAspect(string Title);
```

`AddAsterCore()` registers an in-memory runtime. Add a persistence provider such as `Aster.Persistence.SqliteJson` for durable storage.

Documentation: https://github.com/valence-works/aster
