# Quickstart: Lifecycle Restore Workflows

## Register Services

Use the existing core or provider registration path.

```csharp
var services = new ServiceCollection();
services.AddAsterCore();
// Optional provider replacement:
// services.AddAsterSqliteJson(options => options.ConnectionString = "Data Source=aster.db");

await using var provider = services.BuildServiceProvider();
```

## Mark A Resource

```csharp
var markers = provider.GetRequiredService<IResourceLifecycleMarkerService>();
var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
{
    ResourceId = "product-1",
    State = ResourceLifecycleMarkerState.Archived,
    MarkedAt = DateTimeOffset.UtcNow,
    Reason = "No longer sold",
});
```

## Preview Restore

```csharp
var preview = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
{
    Candidates =
    [
        new ResourceLifecycleRestoreCandidate
        {
            ResourceId = "product-1",
            ExpectedState = ResourceLifecycleMarkerState.Archived,
        },
    ],
});

foreach (var candidate in preview.Candidates)
{
    Console.WriteLine($"{candidate.ResourceId}: {candidate.Status}");
}
```

Expected behavior:

- `Restorable` means the marker exists and matches the expected state.
- `AlreadyRestored` means the resource exists and no marker is present.
- `Failed` includes diagnostics for invalid input, missing target, unsupported state, or marker mismatch.
- Preview does not clear marker state.

## Apply Restore

```csharp
var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
{
    Candidates =
    [
        new ResourceLifecycleRestoreCandidate
        {
            ResourceId = "product-1",
            ExpectedState = ResourceLifecycleMarkerState.Archived,
        },
    ],
});

var restored = result.Candidates.Single();
Console.WriteLine($"{restored.ResourceId}: {restored.Status}");
```

Expected behavior:

- `Restored` means the expected marker was cleared.
- `AlreadyRestored` means the resource already had no marker.
- Marker mismatch fails and leaves the current marker untouched.
- Resource versions and activation state are unchanged.

## Query After Restore

```csharp
var query = provider.GetRequiredService<IResourceQueryService>();
var archived = await query.QueryAsync(new ResourceQuery
{
    LifecycleState = ResourceLifecycleMarkerState.Archived,
});
```

After successful restore, the restored resource no longer appears in archived or soft-deleted lifecycle-state query results.

## Tenant-Scoped Restore

```csharp
await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
{
    TenantScope = new TenantScope("tenant-a"),
    Candidates =
    [
        new ResourceLifecycleRestoreCandidate
        {
            ResourceId = "shared-product",
            ExpectedState = ResourceLifecycleMarkerState.SoftDeleted,
        },
    ],
});
```

Only marker state inside the effective tenant is considered or cleared.

## Non-Goals

Restore workflows do not:

- run automatically;
- authorize operators;
- rewrite resource versions;
- alter activation state;
- delete resources;
- prune versions;
- add lifecycle hooks;
- expose raw SQL or public `IQueryable<Resource>`.
