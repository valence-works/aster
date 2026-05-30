# Quickstart: Batch Version History Inspection

This example shows the intended host flow for inspecting several selected resource histories.

## Register Aster

```csharp
var services = new ServiceCollection()
    .AddAsterCore();

using var provider = services.BuildServiceProvider();
```

SQLite JSON hosts continue to use the existing provider registration:

```csharp
var services = new ServiceCollection()
    .AddAsterSqliteJson(connectionString);
```

No new provider registration, schema migration, query planner, or storage setup is required.

## Request Histories

```csharp
var historyService = provider.GetRequiredService<IResourceVersionHistoryService>();

var result = await historyService.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
{
    TenantScope = TenantScope.Default,
    ResourceIds = ["product-1", "product-2", "product-1", "missing-product"],
});
```

Expected behavior:

- `result.TenantScope` is the effective tenant.
- `result.Histories` contains histories for `product-1`, `product-2`, and `missing-product` in that order.
- `product-1` appears once because duplicate IDs are collapsed.
- `missing-product` appears with an empty `Versions` list.
- Each returned history has the same per-version semantics as `GetHistoryAsync`.

## Verify Against Single-Resource Semantics

```csharp
var batch = await historyService.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
{
    ResourceIds = ["product-1"],
});

var single = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest
{
    ResourceId = "product-1",
});

Debug.Assert(batch.Histories.Single().ResourceId == single.ResourceId);
Debug.Assert(batch.Histories.Single().Versions.Select(static x => x.Version)
    .SequenceEqual(single.Versions.Select(static x => x.Version)));
```

## Empty Selection

```csharp
var empty = await historyService.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
{
    ResourceIds = [],
});
```

Expected behavior:

- The request succeeds.
- `empty.Histories` is empty.

## Invalid Selection

```csharp
await historyService.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
{
    ResourceIds = ["product-1", " "],
});
```

Expected behavior:

- The request fails fast because blank resource identifiers are invalid request shape.
