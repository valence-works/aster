# Data Model: SQLite Schema Idempotency Hardening

## SQLite Initialization Scenario

Test scenario covering repeated construction of SQLite-backed services against the same database path.

Attributes:

- Database path
- Provider construction count
- Persisted definition/resource/activation/marker fixtures
- Table metadata observations

## Legacy Tenant Upgrade Scenario

Test scenario covering pre-tenant SQLite tables upgraded into tenant-aware tables.

Attributes:

- Legacy table definitions
- Legacy row counts
- Post-upgrade tenant-aware row counts
- Bootstrap table absence

## No-Initialization Scenario

Test scenario covering explicit `InitializeSchema = false` behavior.

Attributes:

- Database path
- Provider options
- Resolved provider identity/capabilities
- Database file existence

## State and Lifecycle

No product data model changes are introduced. These are test scenario concepts only.
