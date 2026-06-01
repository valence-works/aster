# Research: SQLite Schema Idempotency Hardening

## Decision: Add Test Coverage Before Production Changes

**Decision**: Implement this slice as focused SQLite tests first; change production schema code only if those tests expose a defect.

**Rationale**: The roadmap calls for operational hardening. Current schema code already uses `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, and primary-key inspection, so the right next step is to pin behavior down with regression coverage.

## Decision: Verify Persisted State and Table Shape

**Decision**: Repeated initialization tests must assert persisted definitions, resources, activation state, lifecycle markers, and tenant-aware primary keys.

**Rationale**: Absence of exceptions is weak evidence. Operational idempotency requires final data and table metadata to remain correct after restarts.

## Decision: Exercise Legacy Upgrade Reruns

**Decision**: Create pre-tenant legacy tables, initialize once, initialize again, then assert default-tenant rows are not duplicated and temporary bootstrap tables are gone.

**Rationale**: Legacy upgrade is the most migration-like path in the SQLite provider. Repeated startup after upgrade must be safe.

## Decision: Preserve InitializeSchema=false

**Decision**: Keep existing no-initialization behavior in scope as a compatibility assertion.

**Rationale**: Operational simplicity depends on explicit startup side effects. Identity/capability resolution should not create a database when initialization is disabled.

## Decision: No New Dependencies or Benchmarks

**Decision**: Use existing xUnit and `Microsoft.Data.Sqlite` test usage only; do not add benchmark infrastructure.

**Rationale**: This is correctness hardening, not performance measurement.
