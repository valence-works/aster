# Aster.Persistence.SqliteJson

SQLite JSON persistence primitives for Aster.

This package currently provides:

- `SqliteJsonResourceStore`
- `SqliteJsonQueryService`
- `IResourceDefinitionStore`
- `IResourceVersionReader`
- `IResourceVersionWriter`
- `IResourceQueryService`

It persists resource definitions, resource version snapshots, and activation state using SQLite tables with JSON payload columns.
Query execution is provider-backed: `ResourceQuery` ASTs are translated to parameterized SQLite SQL and JSON1 expressions instead of materializing the full store in memory.

```csharp
services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});
```

Use this after `AddAsterCore()` to replace the default in-memory definition, version, and query primitives while keeping the provider-backed `DefaultResourceManager`.

Supported query shapes:

- `Latest`, `AllVersions`, `Active`, and `Draft` scopes (`Active` requires `ActivationChannel`)
- `DefinitionId` shortcut filtering
- metadata filters over `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, and `Created`
- metadata sorting over the same fields
- `Skip` and `Take`
- aspect presence checks
- scalar facet `Equals`, string `Contains`, and numeric `Range`
- `And`, `Or`, and single-operand `Not`

Unsupported query shapes throw `UnsupportedQueryFeatureException` with an actionable message. Facet sorting, unknown metadata fields, empty ranges, negative paging values, and date-like facet ranges are intentionally out of scope for this phase.
