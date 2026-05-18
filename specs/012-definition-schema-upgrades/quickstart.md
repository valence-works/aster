# Quickstart: Definition Schema Versions & Upgrade Flow

## Create Resources Across Definition Versions

```csharp
var definitionStore = serviceProvider.GetRequiredService<IResourceDefinitionStore>();
var manager = serviceProvider.GetRequiredService<IResourceManager>();

await definitionStore.RegisterDefinitionAsync(
    new ResourceDefinitionBuilder()
        .WithDefinitionId("Product")
        .WithAspect<TitleAspect>()
        .Build());

var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new()
    {
        ["TitleAspect"] = new TitleAspect("Alpha"),
    },
});

await definitionStore.RegisterDefinitionAsync(
    new ResourceDefinitionBuilder()
        .WithDefinitionId("Product")
        .WithAspect<TitleAspect>()
        .WithAspect<SearchAspect>()
        .Build());

var v2Product = await manager.CreateAsync("Product", new CreateResourceRequest());
```

`v1.DefinitionVersion` remains `1`. `v2Product.DefinitionVersion` records the latest definition version at creation time.

## Inspect One Resource Version

```csharp
var schemaVersions = serviceProvider.GetRequiredService<IResourceSchemaVersionService>();
var status = await schemaVersions.GetSchemaStatusAsync(v1);

Console.WriteLine(status.Status);
Console.WriteLine(status.RecordedDefinitionVersion);
Console.WriteLine(status.LatestDefinitionVersion);
```

Status is per resource version. It does not summarize every version of the resource.

## Upgrade Explicitly

```csharp
var latest = await manager.GetLatestVersionAsync(v1.ResourceId);

var upgrade = await schemaVersions.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
{
    BaseVersion = latest!.Version,
    TargetDefinitionVersion = 2,
    AspectUpdates = new()
    {
        ["SearchAspect"] = new SearchAspect("alpha"),
    },
});

if (upgrade.Status == ResourceSchemaUpgradeStatus.Upgraded)
{
    Console.WriteLine(upgrade.Resource!.Version);
    Console.WriteLine(upgrade.Resource.DefinitionVersion);
}
```

Upgrades append a new immutable resource version. Previous versions keep their original definition version lineage.

## Carried-Forward Data

If a target definition no longer declares an aspect key present on the source resource version, the upgrade preserves that aspect data by default:

```csharp
foreach (var key in upgrade.CarriedForwardAspectKeys)
{
    Console.WriteLine($"Preserved undeclared aspect: {key}");
}
```

The caller may explicitly replace aspect data by including the aspect key in `AspectUpdates`.

## No Automatic Rewrites

Registering a new definition version does not update existing resources. Callers inspect schema status and request upgrades explicitly.
