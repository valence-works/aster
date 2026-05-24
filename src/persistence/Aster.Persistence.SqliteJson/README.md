# Aster.Persistence.SqliteJson

SQLite JSON persistence primitives for Aster.

This package currently provides:

- `SqliteJsonResourceStore`
- `SqliteJsonQueryService`
- `IResourceDefinitionStore`
- `IResourceVersionReader`
- `IResourceVersionWriter`
- `IResourcePortabilityStore`
- `IResourceQueryService`
- `IResourceQueryCapabilitiesProvider`

It persists resource definitions, resource version snapshots, and activation state using SQLite tables with JSON payload columns.
Query execution is provider-backed: `ResourceQuery` ASTs are translated to parameterized SQLite SQL and JSON1 expressions instead of materializing the full store in memory.
Portability support is provider-backed too: the SQLite JSON resource store implements exact snapshot reads, target-state comparison reads, and atomic import apply for `IResourcePortabilityService`.
All provider tables include `tenant_id`; fresh databases use tenant-aware primary keys, and provider initialization adds a default-scope `tenant_id` column to pre-tenant tables so existing rows remain visible to omitted-scope callers.

```csharp
services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});
```

Use this after `AddAsterCore()` to replace the default in-memory definition, version, and query primitives while keeping the provider-backed `DefaultResourceManager`.

Portability service registration remains in core. After `AddAsterSqliteJson(...)`, that same service uses the SQLite-backed `IResourcePortabilityStore`:

```csharp
var portability = serviceProvider.GetRequiredService<IResourcePortabilityService>();

var export = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    TenantScope = TenantScope.FromTenantId("tenant-a"),
    ScopeMode = PortableExportScopeMode.SelectedResources,
    ResourceIds = ["product-1"],
    ResourceVersionScope = PortableResourceVersionScope.AllVersions,
});

if (export.Snapshot is null)
    return; // export failed; inspect export.Diagnostics

var preview = await portability.PreviewImportAsync(
    export.Snapshot,
    new PortableImportOptions
    {
        TargetTenantScope = TenantScope.FromTenantId("tenant-b"),
    });
```

SQLite import apply is all-or-nothing for a planned snapshot. Strict imports fail before writing on divergent identity collisions; explicit `RemapDivergent` mode writes deterministic remapped identifiers and keeps definition lineage, resource versions, and activation entries consistent. No SQLite schema migration or physical indexing is introduced by portability primitives.

Supported query shapes:

- `Latest`, `AllVersions`, `Active`, and `Draft` scopes (`Active` requires `ActivationChannel`)
- `DefinitionId` shortcut filtering
- metadata filters over `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, and `Created`
- metadata sorting over the same fields
- facet sorting over scalar facet values
- `Skip` and `Take`
- aspect presence checks
- metadata/facet `Equals`, `NotEquals`, and `In`
- string `Contains` and `StartsWith`
- facet `Exists`
- numeric/date-like facet `Range`
- `And`, `Or`, and single-operand `Not`

Date-like facet ranges match JSON string scalar values in the ISO-8601-style shape emitted by `System.Text.Json` for `DateTime` or `DateTimeOffset`. Date-only strings, malformed strings, numbers, booleans, objects, arrays, nulls, and missing facets do not match date-like range predicates.

Unsupported query shapes throw `UnsupportedQueryFeatureException` with stable `Code`, `Feature`, optional `Path`, and an actionable message. Metadata range filters, unknown metadata fields, empty ranges, negative paging values, mixed range bound shapes, and invalid date-like range bounds are intentionally rejected.

The provider also declares this support through `SqliteJsonQueryCapabilitiesProvider` with provider key `sqlite-json`, so callers can inspect capabilities or use `IResourceQueryValidator` before execution. Execution runs shared validation first and remains authoritative if validation is skipped.

Tenant scope is applied as a provider-owned predicate before user query predicates. Latest, active, and draft scopes all include tenant-aware joins so matching resource IDs or activation channels in another tenant cannot affect results.
