# Aster.Persistence.SqlServerJson

SQL Server storage for [Aster](https://github.com/valence-works/aster): resource definitions, shared aspect definitions, immutable resource versions, activation state, lifecycle markers, version pruning and portable import/export, with JSON payloads in `nvarchar(max)` read through `OPENJSON` (ADR 0004; projection indexes and text comparison per ADR 0006).

```csharp
services.AddAsterCore();
services.AddAsterSqlServerJson(options =>
{
    options.ConnectionString = "Server=...;Database=aster;User Id=aster;Password=...;Encrypt=True";
});
```

Requires SQL Server 2019 or later, at database compatibility level 130 or higher. The native `json` type isn't used, so SQL Server 2019 and 2022 work as they are.

- **Schema:** the provider's tables are a list of ordered infrastructure steps, recorded in `aster_infrastructure_steps` as they are applied. `IInfrastructureRunner` applies, lists and verifies them.
  - **Automatic (default):** with `InitializeSchema = true`, the provider applies pending steps before its first statement; resolving the stores does no I/O, and requests that arrive during the run wait for it without blocking a thread. Runs are serialized by an exclusive application lock (`sp_getapplock`, owned by the run's transaction), so any number of processes can start against a fresh or outdated database at once; each step runs once, and a failed run rolls back as a whole. Existing tables and data are never dropped or rewritten.
  - **Explicit (recommended for production):** set `InitializeSchema = false` so the application never changes the schema, and apply steps from a deployment step with an account allowed to run DDL:

    ```csharp
    var runner = app.Services.GetRequiredService<IInfrastructureRunner>();
    await runner.ApplyAsync();                    // returns the steps it applied

    // At application startup, before serving requests:
    var check = await runner.VerifyAsync();
    if (!check.IsCurrent)
        throw new InvalidOperationException("The Aster SQL Server schema is not current; apply the pending infrastructure steps.");
    ```

    `VerifyAsync` also reports steps applied by a newer version of the provider than the one running.
  - The tables are created in the connecting user's default schema; the date-key function the queries share is `dbo.aster_date_key` (scalar functions must be called by a two-part name). Applying fails fast below compatibility level 130, which `OPENJSON` needs.
- **Tenancy:** every table is keyed by `tenant_id`.
- **Keys:** identifier columns are `nvarchar` with the `Latin1_General_100_BIN2` collation, so they compare and sort by code point, as the other providers do. Tenant IDs hold up to 128 characters, resource, definition and aspect IDs up to 256, activation channels up to 64; longer values are rejected by the database.
- **No trailing spaces in identifiers:** SQL Server compares strings as if padded with trailing spaces, even under a binary collation, so `x` and `x ` would be the same key. The provider refuses a tenant, resource, definition, aspect or channel ID that ends in a space with an `ArgumentException`, on reads, writes and deletes alike, so `tenant-a ` can never reach `tenant-a`'s data. A check constraint backs this up in the schema. Trim identifiers before calling the SDK.
- **Concurrency:** versions are keyed by `(tenant_id, resource_id, version)`. A writer that loses a race on the same base version gets `ConcurrencyException`; it never produces a duplicate version. Registrations, activations and pruning of one key serialize on an application lock.
- **Queries:** `IResourceQueryService` translates each `ResourceQuery` to one parameterized SQL statement.
  - It supports the same portable surface as SQLite JSON and PostgreSQL JSONB: every scope, metadata and facet filters, ranges, array and reference operators, lifecycle state, sorting and paging. The shared conformance suite checks that every provider returns the same results.
  - Facets are read by enumerating keys with `OPENJSON` (which returns values of any length, and arrays) rather than `JSON_VALUE`. Aspect keys, facet IDs and values are always parameters. Metadata fields, scopes and sort directions are mapped from fixed lists, never interpolated.
  - Text comparisons ignore case like .NET's `OrdinalIgnoreCase`, independent of the database's default collation: both sides are upper-cased under `Latin1_General_100_CI_AS_SC` and compared exactly under `Latin1_General_100_BIN2`, and equality also compares lengths, so trailing spaces count. Comparing under a case-insensitive collation instead would equate `ß` with `ss`.
  - Known differences, outside the conformance suite: `Contains`/`StartsWith` over a number see the number as written in the payload (`1.50`, where SQLite sees `1.5`).
  - Without index projections, facet predicates evaluate per row, so a query reads every version in its scope and tenant.
  - The default `READ COMMITTED` isolation lets readers wait on writers; consider `READ_COMMITTED_SNAPSHOT` for read-heavy hosts.

## Index projections

Declare the facets (and metadata fields) your queries filter on, and the provider backs each with an index:

```csharp
services.AddAsterSqlServerJson(options =>
{
    options.ConnectionString = "...";
    options.IndexProjections.Add(IndexProjection.Facet("price", "Price", "Amount", IndexFieldType.Decimal));
    options.IndexProjections.Add(IndexProjection.Facet("tags", "Labels", "Tags", IndexFieldType.KeywordArray));
    options.IndexProjections.Add(IndexProjection.Metadata("owner", "Owner", IndexFieldType.Keyword));
});
```

- **What gets an index:**
  - text, keyword and GUID facets (and any other scalar type): a persisted computed column on `resource_versions` holding the upper-cased first 400 characters of the value (`JSON_VALUE`, or `JSON_QUERY` for arrays and objects), indexed with `tenant_id`; used by text `Equals` and `In`
  - integer and decimal facets: a persisted computed column holding the number as `float`, indexed; used by `Equals`, `In` and `Range`
  - date/time facets: a persisted computed column holding the UTC date key, indexed; used by `Range`
  - `KeywordArray` facets: one row per string element in `aster_projection_elements` (upper-cased, first 400 characters), written by an insert trigger on `resource_versions` and deleted with the version; used by `ArrayContains`, `ArrayContainsAny` and `ArrayContainsAll` with text candidates. A computed column holds one value per row, so arrays can't use one.
  - `ResourceId`, `Id`, `DefinitionId` and `Owner`: a computed column over the upper-cased column, indexed; `Version` and `Created`: an index on the column
- **Results never change.** The translator adds an index-matching condition beside the predicate it accompanies, implied by it, and only where the predicate isn't negated. Keys hold a 400-character prefix (800 bytes, under the 1,700-byte index key limit), so long values match their prefix in the index and then the full predicate. `JSON_VALUE` returns NULL for strings over 4,000 characters, so text conditions are only added for shorter values. Queries the index can't serve still run, as before.
- **Paths:** computed columns read the payload through JSON paths. SQL Server compares a path's keys with the stored, escaped text, so the provider escapes them exactly as its serializer writes them; rows written by other means must use the same escaping (`System.Text.Json` defaults).
- **Lifecycle:** the infrastructure runner creates the columns, indexes and element rows of each declared projection, and removes those of projections no longer declared (`GetPendingAsync` lists both). Objects are named after a hash of their definition, so redeclaring one is a no-op and changing one replaces it. Adding a computed column, building an index or backfilling element rows locks `resource_versions` for its duration; on a large table, apply from a deployment step.
- **One declaration per database:** every process that applies infrastructure against a database must declare the same projections. A process applies its own declaration, so one that declares fewer, such as an older version still running in a rolling deployment, removes the others' indexes. Use `InitializeSchema = false` in application processes and apply from the deployment step.
- **Capabilities:** `QueryCapabilityDescription.IndexProjections` lists the declared, indexed projections. Invalid declarations fail at registration.
