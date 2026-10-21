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
- **Queries:** until the Postgres query translator lands (#98), `IResourceQueryService` is the core's in-memory query service, which reads latest versions through this provider and filters them in process. That is correct but scans the tenant.

Requires PostgreSQL 13 or later.
