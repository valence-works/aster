# Contract: SQLite Schema Idempotency Hardening

## Public Behavior Under Test

SQLite JSON provider registration and construction MUST remain idempotent for existing databases:

- Repeated initialization MUST preserve existing tenant-aware tables and data.
- Legacy pre-tenant tables MUST upgrade to tenant-aware tables once and tolerate later initialization reruns.
- Temporary legacy bootstrap tables MUST NOT remain after successful initialization.
- `InitializeSchema = false` MUST preserve explicit no-database-creation behavior when resolving identity/capabilities only.

## Non-Goals

This feature MUST NOT introduce:

- New public APIs
- Storage schema format changes
- Provider registries
- Public raw SQL surface
- Public `IQueryable<Resource>`
- Query planner behavior
- Runtime scanning or automatic discovery
- Schedulers
- Benchmark infrastructure
- New dependencies
