# Quickstart: Historical Version Activation

## Create Multiple Versions

```csharp
var manager = serviceProvider.GetRequiredService<IResourceManager>();

var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
{
    ResourceId = "product-1",
});

var v2 = await manager.UpdateAsync("product-1", new UpdateResourceRequest
{
    BaseVersion = v1.Version,
});
```

## Activate A Historical Version

```csharp
await manager.ActivateAsync("product-1", v1.Version, "Published");

var active = await manager.GetActiveVersionsAsync("product-1", "Published");
var latest = await manager.GetLatestVersionAsync("product-1");
```

Expected behavior:

- `active.Single().Version == 1`
- `latest!.Version == 2`

## Multi-Active Historical Activation

```csharp
await manager.ActivateAsync("product-1", v2.Version, "Preview", allowMultipleActive: true);
await manager.ActivateAsync("product-1", v1.Version, "Preview", allowMultipleActive: true);

var preview = await manager.GetActiveVersionsAsync("product-1", "Preview");
```

Expected behavior:

- `preview.Select(x => x.Version)` contains `[1, 2]` in deterministic order.

## Tenant-Scoped Historical Activation

```csharp
var tenant = TenantScope.FromTenantId("tenant-a");

await manager.ActivateAsync(
    "product-1",
    v1.Version,
    "Published",
    tenant,
    allowMultipleActive: false,
    CancellationToken.None);
```

Only the selected tenant's activation state is changed.

## Non-Goals

Historical activation does not:

- create a new resource version;
- change latest;
- rewrite resource payloads;
- evaluate policies;
- run background jobs;
- introduce SQL or queryable resource surfaces.
