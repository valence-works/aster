# Quickstart: SQLite JSON Querying (Phase 2A)

## Goal

Run Aster with SQLite JSON persistence and execute provider-backed `ResourceQuery` instances through `IResourceQueryService`.

## Setup

```csharp
using Aster.Core.Extensions;
using Aster.Persistence.SqliteJson;

var services = new ServiceCollection();

services.AddAsterCore();
services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});

await using var provider = services.BuildServiceProvider();
```

`AddAsterSqliteJson(...)` registers SQLite-backed definition, version reader/writer, and query service primitives. `IResourceManager` remains the provider-backed core manager.

## Seed Data

```csharp
var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
var manager = provider.GetRequiredService<IResourceManager>();

await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .Build());

var product = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new Dictionary<string, object>
    {
        ["TitleAspect"] = new { Title = "Super Gadget" },
        ["PriceAspect"] = new { Amount = 49.99m, Currency = "USD" },
    },
});

await manager.ActivateAsync(product.ResourceId, product.Version, "Published");
```

## Query Metadata

```csharp
var queryService = provider.GetRequiredService<IResourceQueryService>();

var latestProducts = await queryService.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    Scope = ResourceVersionScope.Latest,
    Sorts = [new SortExpression("Created", SortDirection.Descending)],
    Take = 20,
});
```

## Query Facets

```csharp
var gadgets = await queryService.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    Scope = ResourceVersionScope.Active,
    ActivationChannel = "Published",
    Filter = new FacetValueFilter(
        "TitleAspect",
        "Title",
        "Gadget",
        ComparisonOperator.Contains),
});
```

## Unsupported Queries

Unsupported query shapes throw `UnsupportedQueryFeatureException`. They do not silently fall back to in-memory filtering.
