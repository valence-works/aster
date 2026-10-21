# Aster.Persistence.PostgresJsonb

PostgreSQL storage for [Aster](https://github.com/valence-works/aster): resource definitions, shared aspect definitions, immutable resource versions, activation state, lifecycle markers, version pruning and portable import/export, with `jsonb` payloads.

```csharp
services.AddAsterCore();
services.AddAsterPostgresJsonb(options =>
{
    options.ConnectionString = "Host=localhost;Database=aster;Username=aster;Password=...";
});
```

- **Schema:** the provider's tables are a list of ordered infrastructure steps, recorded in `aster_infrastructure_steps` as they are applied. `IInfrastructureRunner` applies, lists and verifies them.
  - **Automatic (default):** with `InitializeSchema = true`, the first store resolved applies pending steps. Runs are serialized by an advisory lock, so any number of processes can start against a fresh or outdated database at once; each step runs once, and a failed run rolls back as a whole.
  - **Explicit (recommended for production):** set `InitializeSchema = false` so the application never changes the schema, and apply steps from a deployment step with an account allowed to run DDL:

    ```csharp
    var runner = app.Services.GetRequiredService<IInfrastructureRunner>();
    await runner.ApplyAsync();                    // returns the steps it applied
    var check = await runner.VerifyAsync();       // at startup: fail fast if !check.IsCurrent
    ```

    `VerifyAsync` also reports steps applied by a newer version of the provider than the one running.
- **Tenancy:** every table is keyed by `tenant_id`.
- **Concurrency:** versions are keyed by `(tenant_id, resource_id, version)`. A writer that loses a race on the same base version gets `ConcurrencyException`; it never produces a duplicate version.
- **Queries:** `IResourceQueryService` translates each `ResourceQuery` to one parameterized SQL statement over `jsonb`.
  - It supports the same portable surface as SQLite JSON: every scope, metadata and facet filters, ranges, array operators, lifecycle state, sorting and paging. A shared conformance suite checks that in-memory, SQLite and Postgres return the same results.
  - Known differences from SQLite, outside that suite: `Contains`/`StartsWith` over a number see `jsonb`'s rendering (`1e5` is `100000` here, `100000.0` in SQLite), text over an array or object facet sees `jsonb`'s spacing, and `Created` keeps microseconds (SQLite's text keeps a seventh digit).
  - Aspect keys, facet IDs and values are always parameters. Metadata fields, scopes and sort directions are mapped from fixed lists, never interpolated.
  - Facet predicates still evaluate per row, so a query reads every version in its scope and tenant. Index projections are #100.
  - Text comparisons ignore case like .NET's `OrdinalIgnoreCase`, upper-casing under the database's default collation. A database whose `LC_CTYPE` is plain `C` folds ASCII letters only.

Requires PostgreSQL 13 or later.
