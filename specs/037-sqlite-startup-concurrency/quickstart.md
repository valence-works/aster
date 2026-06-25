# Quickstart: SQLite Startup Concurrency Hardening

## Focused Validation

Run the new SQLite startup concurrency coverage:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonStartupConcurrencyTests"
```

Run adjacent SQLite schema and tenant tests:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~SqliteJsonSchemaIdempotencyTests|FullyQualifiedName~SqliteJsonTenantScopeTests"
```

## Full Validation

Run the full solution test and build gates:

```bash
dotnet test Aster.sln
dotnet build Aster.sln /m:1
```

## Expected Result

- Concurrent fresh startup succeeds and leaves the database usable.
- Concurrent existing startup preserves seeded state and tenant-aware shape.
- Concurrent no-schema construction does not create a database file.
- No production public surface, storage schema, dependency, scheduler, benchmark, query planner, public SQL, or public `IQueryable<Resource>` changes are required unless a demonstrated defect needs the smallest possible fix.
