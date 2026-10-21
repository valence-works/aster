# Aster.Persistence.PostgresJsonb

PostgreSQL storage for [Aster](https://github.com/valence-works/aster): resource definitions, shared aspect definitions, immutable resource versions, activation state, lifecycle markers, version pruning and portable import/export, with `jsonb` payloads.

```csharp
services.AddAsterCore();
services.AddAsterPostgresJsonb(options =>
{
    options.ConnectionString = "Host=localhost;Database=aster;Username=aster;Password=...";
});
```

- **Schema:** with `InitializeSchema` (the default), the provider creates its tables on first use. Bootstrap is idempotent and serialized by an advisory lock, so several processes can start against a fresh database at once. Set it to `false` when the schema is managed out of band.
- **Tenancy:** every table is keyed by `tenant_id`.
- **Concurrency:** versions are keyed by `(tenant_id, resource_id, version)`. A writer that loses a race on the same base version gets `ConcurrencyException`; it never produces a duplicate version.
- **Queries:** `IResourceQueryService` translates each `ResourceQuery` to one parameterized SQL statement over `jsonb`.
  - It supports the same portable surface as SQLite JSON: every scope, metadata and facet filters, ranges, array operators, lifecycle state, sorting and paging. A shared conformance suite checks that in-memory, SQLite and Postgres return the same results.
  - Known differences from SQLite, outside that suite: `Contains`/`StartsWith` over a number see `jsonb`'s rendering (`1e5` is `100000` here, `100000.0` in SQLite), text over an array or object facet sees `jsonb`'s spacing, and `Created` keeps microseconds (SQLite's text keeps a seventh digit).
  - Aspect keys, facet IDs and values are always parameters. Metadata fields, scopes and sort directions are mapped from fixed lists, never interpolated.
  - Facet predicates still evaluate per row, so a query reads every version in its scope and tenant. Index projections are #100.
  - Text comparisons ignore case like .NET's `OrdinalIgnoreCase`, upper-casing under the database's default collation. A database whose `LC_CTYPE` is plain `C` folds ASCII letters only.

Requires PostgreSQL 13 or later.
