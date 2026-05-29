# Quickstart: Resource Version History Inspection

This quickstart shows the intended host workflow for read-only version history.

## Register Aster

```csharp
var services = new ServiceCollection()
    .AddAsterCore();

using var provider = services.BuildServiceProvider();
```

SQLite JSON hosts use the existing provider registration:

```csharp
var services = new ServiceCollection()
    .AddAsterCore()
    .AddAsterSqliteJson(options =>
    {
        options.ConnectionString = "Data Source=aster.db";
    });
```

## Create Versions And Activation State

```csharp
var manager = provider.GetRequiredService<IResourceManager>();

var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
{
    ResourceId = "product-1",
    Owner = "host",
});

var v2 = await manager.UpdateAsync("product-1", new UpdateResourceRequest
{
    BaseVersion = v1.Version,
    Owner = "host",
});

var v3 = await manager.UpdateAsync("product-1", new UpdateResourceRequest
{
    BaseVersion = v2.Version,
    Owner = "host",
});

await manager.ActivateAsync("product-1", v2.Version, "Published");
```

## Inspect History

```csharp
var history = provider.GetRequiredService<IResourceVersionHistoryService>();

var result = await history.GetHistoryAsync(new ResourceVersionHistoryRequest
{
    ResourceId = "product-1",
});

foreach (var version in result.Versions)
{
    Console.WriteLine(
        $"{version.Version}: latest={version.IsLatest}, draft={version.IsDraft}, " +
        $"channels={string.Join(",", version.ActiveChannels)}, maintenance={version.MaintenanceDisposition}");
}
```

Expected behavior:

- version 1 is historical, inactive, and a possible maintenance candidate;
- version 2 is active in `Published` and protected;
- version 3 is latest and protected.

## Tenant-Scoped Inspection

```csharp
var tenantHistory = await history.GetHistoryAsync(new ResourceVersionHistoryRequest
{
    TenantScope = TenantScope.ForTenant("tenant-a"),
    ResourceId = "product-1",
});
```

Only versions, activation states, and lifecycle marker state from `tenant-a` are considered. Matching identifiers in other tenants do not appear.

## Non-Goals

History inspection does not:

- mutate resources, activation, lifecycle markers, or policies;
- evaluate pruning policy eligibility;
- perform restore or pruning;
- run automatic background maintenance;
- expose SQL or `IQueryable<Resource>`.
