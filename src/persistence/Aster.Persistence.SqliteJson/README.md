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

Current boundary: this package supplies the low-level provider contracts. `IResourceManager` is still implemented by the in-memory manager, so a provider-backed manager/refactor is the next integration slice.
