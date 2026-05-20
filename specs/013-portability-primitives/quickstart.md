# Quickstart: Portability Primitives

## Register Core Services

```csharp
services.AddAsterCore();
```

SQLite-backed hosts continue to register the provider after core:

```csharp
services.AddAsterCore();
services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});
```

## Export Selected Resources

```csharp
var portability = serviceProvider.GetRequiredService<IResourcePortabilityService>();

var export = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    ScopeMode = PortableExportScopeMode.SelectedResources,
    ResourceIds = ["product-1", "product-2"],
    ResourceVersionScope = PortableResourceVersionScope.AllVersions,
});

if (export.Diagnostics.Any(d => d.Severity == PortableDiagnosticSeverity.Error))
{
    foreach (var diagnostic in export.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

    return;
}

var snapshot = export.Snapshot;
```

The snapshot includes selected resource versions, referenced definition versions, and activation entries for exported versions. If an active version is omitted by scope, the export reports `skipped-activation-entry`.

## Preview Import

```csharp
var preview = await portability.PreviewImportAsync(snapshot);

foreach (var diagnostic in preview.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");

if (!preview.CanImport)
    return;

foreach (var mapping in preview.IdentityMap)
    Console.WriteLine($"{mapping.EntityKind}: {mapping.SourceId} -> {mapping.TargetId} ({mapping.Reason})");
```

Default import behavior is strict. Identical existing content is treated as already satisfied. Divergent collisions fail before writing unless remap mode is selected.

## Preview With Explicit Remapping

```csharp
var remapPreview = await portability.PreviewImportAsync(
    snapshot,
    new PortableImportOptions
    {
        CollisionMode = PortableImportCollisionMode.RemapDivergent,
    });
```

The same snapshot and target state produce the same identity map during preview and write import.
Definition remaps update imported resource lineage references. Resource remaps update imported resource versions and activation entries together.

## Write Import

```csharp
var result = await portability.ImportAsync(
    snapshot,
    new PortableImportOptions
    {
        CollisionMode = PortableImportCollisionMode.RemapDivergent,
    });

if (result.Status == PortableImportStatus.Failed)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

Write import is all-or-nothing. Failed imports leave no partial definitions, resources, versions, or activation entries.

## Definition-Only Export

```csharp
var definitionExport = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    ScopeMode = PortableExportScopeMode.DefinitionsOnly,
    DefinitionIds = ["Product"],
});
```

## Definition With Resources Export

```csharp
var fullDefinitionExport = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    ScopeMode = PortableExportScopeMode.DefinitionWithResources,
    DefinitionIds = ["Product"],
    ResourceVersionScope = PortableResourceVersionScope.LatestOnly,
});
```

This includes resources for the selected definitions, latest resource versions, referenced definition versions, and activation entries only for exported versions.
