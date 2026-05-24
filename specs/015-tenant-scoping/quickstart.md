# Quickstart: Tenant Scoping

This quickstart shows the tenant-scoped SDK behavior.

## Default Single-Tenant Behavior

Existing callers do not need to provide tenant scope:

```csharp
builder.Services.AddAsterCore();

var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .Build();

await definitionStore.RegisterDefinitionAsync(definition);

var product = await manager.CreateAsync("Product", new CreateResourceRequest
{
    ResourceId = "product-1",
});
```

These operations use the documented default single-tenant scope.

## Explicit Tenant Scope

Hosts that need tenant isolation pass a tenant scope explicitly:

```csharp
var tenantA = TenantScope.FromTenantId("tenant-a");
var tenantB = TenantScope.FromTenantId("tenant-b");

await definitionStore.RegisterDefinitionAsync(productDefinition, tenantA);
await definitionStore.RegisterDefinitionAsync(productDefinition, tenantB);

var productA = await manager.CreateAsync("Product", new CreateResourceRequest
{
    TenantScope = tenantA,
    ResourceId = "product-1",
});

var productB = await manager.CreateAsync("Product", new CreateResourceRequest
{
    TenantScope = tenantB,
    ResourceId = "product-1",
});
```

The same definition and resource IDs can exist in both tenants.

## Tenant-Scoped Query

Queries select one effective tenant:

```csharp
var results = await queryService.QueryAsync(new ResourceQuery
{
    TenantScope = tenantA,
    DefinitionId = "Product",
    Scope = ResourceVersionScope.Latest,
});
```

Results include only resources from `tenant-a`.

## Tenant-Scoped Activation

Activation state is isolated by tenant:

```csharp
await manager.ActivateAsync(
    resourceId: "product-1",
    version: 1,
    channel: "Published",
    tenantScope: tenantA);

var active = await manager.GetActiveVersionsAsync(
    resourceId: "product-1",
    channel: "Published",
    tenantScope: tenantA);
```

An activation in `tenant-a` does not affect `tenant-b`, even when resource IDs and channel names match.

## Tenant-Scoped Portability

Exports record the source tenant:

```csharp
var export = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    TenantScope = tenantA,
    ScopeMode = PortableExportScopeMode.SelectedResources,
    ResourceIds = ["product-1"],
    ResourceVersionScope = PortableResourceVersionScope.AllVersions,
});

var snapshot = export.Snapshot!;
Console.WriteLine(snapshot.SourceTenantScope.TenantId); // tenant-a
```

Imports target one explicit tenant:

```csharp
var preview = await portability.PreviewImportAsync(
    snapshot,
    new PortableImportOptions
    {
        TargetTenantScope = tenantB,
    });
```

Preview and import diagnostics report both source and target tenant metadata.

## Lifecycle Hooks

Hook contexts expose the same effective tenant used by the operation:

```csharp
public sealed class TenantAuditHook : ResourceLifecycleHook
{
    public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        var tenant = context.TenantScope;
        return ValueTask.FromResult(LifecycleHookOutcome.Continue());
    }
}
```

Hooks do not discover tenant state through ambient context.

## Failure Cases

Tenant scope construction rejects blank IDs immediately:

```csharp
var invalid = TenantScope.FromTenantId(" ");
```

This throws `ArgumentException`. Operation-boundary validation still fails closed with stable codes when a malformed scope instance is supplied on a request, query, snapshot, or import option. Expected failure codes include:

- `tenant-scope-invalid`
- `tenant-scope-mismatch`
- `tenant-scope-required`
- `invalid-tenant-scope`
- `source-tenant-scope-mismatch`

## Exclusions

This slice does not add shared definitions, tenant hierarchy, cross-tenant queries, authorization, policies, migrations, runtime scanning, provider registries, public SQL, or public `IQueryable<Resource>`.
