# Aster.Persistence.SqliteJson

SQLite JSON persistence primitives for Aster.

This package currently provides:

- `SqliteJsonResourceStore`
- `IResourceDefinitionStore`
- `IResourceVersionReader`
- `IResourceVersionWriter`

It persists resource definitions, resource version snapshots, and activation state using SQLite tables with JSON payload columns.

```csharp
services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});
```

Use this after `AddAsterCore()` to replace the default in-memory definition and version primitives while keeping the provider-backed `DefaultResourceManager` and query service orchestration.
