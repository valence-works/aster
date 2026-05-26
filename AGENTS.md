<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read `specs/016-policy-foundations/plan.md`.
<!-- SPECKIT END -->

## Active Technologies
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies (009-portable-operators)
- Existing resource payloads; no migration (009-portable-operators)
- Existing resource JSON payloads; no migration or schema changes (010-sqlite-date-ranges)
- Existing resource JSON payloads; no schema migration or physical index creation (011-explicit-indexing-model)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, resource manager/store abstractions, SQLite JSON provider, xUnit test stack; no new dependencies (012-definition-schema-upgrades)
- Existing resource JSON payloads and definition versions; no schema migration or automatic data rewrite (012-definition-schema-upgrades)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, resource definition/resource version abstractions, in-memory store, SQLite JSON provider, xUnit test stack; no new dependencies (013-portability-primitives)
- Existing resource definitions, resource versions, and activation state; no schema migration planned for SQLite JSON or in-memory stores (013-portability-primitives)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, resource manager/store abstractions, resource schema-version service, portability service, SQLite JSON provider, xUnit test stack; no new dependencies (014-host-lifecycle-hooks)
- Existing resource definitions, resource versions, activation state, and portable snapshots; no schema migration or persisted hook state (014-host-lifecycle-hooks)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, in-memory store, SQLite JSON provider, resource manager/store abstractions, query capability/validation stack, portability service, lifecycle hook dispatcher, xUnit test stack; no new dependencies (015-tenant-scoping)
- Existing resource definitions, resource versions, activation state, and portable snapshots extended with tenant scope metadata; no automatic migration policy or external storage service (015-tenant-scoping)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, in-memory store, SQLite JSON provider, resource manager/store abstractions, query capability/validation stack, portability service, lifecycle hook dispatcher, xUnit test stack; no new dependencies (016-policy-foundations)
- Existing resource definitions gain policy declaration metadata; resource lifecycle markers are stored as additive state separate from immutable resource versions; portable snapshots include policy declarations and lifecycle markers; SQLite JSON adds policy/marker storage without a general migration framework (016-policy-foundations)
- C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, policy declaration/preview models, lifecycle marker service/store, resource definition store, resource version reader, in-memory store, SQLite JSON provider through existing abstractions, xUnit test stack; no new dependencies (017-policy-application-orchestration)
- No schema or storage changes. Application orchestration writes only existing lifecycle marker state through `IResourceLifecycleMarkerService`; definitions, resources, activation state, portability snapshots, and SQLite tables remain unchanged. (017-policy-application-orchestration)

## Recent Changes
- 016-policy-foundations: Added explicit policy foundations planning for definition-attached policy declarations, deterministic previews, archive/soft-delete lifecycle markers, lifecycle-state queries, portability, and provider support; no automatic execution, pruning writes, scheduler, policy engine, provider registry, public SQL, or public IQueryable<Resource>
- 015-tenant-scoping: Added explicit tenant boundary planning for definitions, resources, activation state, queries, schema upgrades, portability, and lifecycle hooks; no automatic migration policy or tenant registry
- 014-host-lifecycle-hooks: Added explicit host lifecycle hook planning over save, activation, deactivation, export, preview import, and write import; no schema migration or persisted hook state
- 013-portability-primitives: Added deterministic export/import portability planning over existing definitions, resources, and activation state; no schema migration planned
- 012-definition-schema-upgrades: Added definition schema version and explicit upgrade flow planning; no schema migration or automatic data rewrite
- 011-explicit-indexing-model: Added explicit provider-declared indexing model planning for core SDK contracts; no storage migration or physical indexing
- 009-portable-operators: Added C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting + Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies
