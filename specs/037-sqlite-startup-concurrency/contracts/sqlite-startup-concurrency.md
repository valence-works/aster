# Contract: SQLite Startup Concurrency Hardening

This slice defines behavior under test for SQLite JSON startup. It does not add public product APIs.

## Fresh Database Startup

Concurrent provider initialization against the same fresh database path MUST:

- complete for every startup attempt,
- leave the SQLite JSON schema usable,
- allow persisted definitions and resources to be saved and read through existing APIs after startup,
- avoid public SQL, public `IQueryable<Resource>`, provider registries, runtime scanning, automatic discovery, schedulers, benchmarks, and new dependencies.

## Existing Database Startup

Concurrent provider initialization against the same existing tenant-aware database MUST:

- preserve existing definitions,
- preserve existing resource versions,
- preserve existing activation state,
- preserve existing lifecycle markers,
- preserve tenant-aware table metadata,
- avoid duplicate rows and bootstrap-only leftover tables in the covered scenario.

## No-Schema Mode

Concurrent service construction with schema initialization disabled MUST remain passive:

- constructing identity/capability/provider services MUST NOT create a SQLite database file,
- no schema mutation is allowed while `InitializeSchema = false`,
- existing explicit options remain the only control surface.

## Non-Goals

- No public API additions.
- No storage schema changes.
- No provider registry.
- No runtime scanning or automatic discovery.
- No query planner behavior changes.
- No public raw SQL surface.
- No public `IQueryable<Resource>`.
- No scheduler or background worker.
- No benchmark infrastructure.
- No new dependencies.
