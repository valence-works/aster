# Research: SQLite Startup Concurrency Hardening

## Decision: Use Bounded Concurrent Startup Tests

**Decision**: Use a small fixed number of concurrent provider constructions against the same temporary SQLite path rather than benchmark-style load tests.

**Rationale**: The product risk is startup correctness, not throughput measurement. A bounded test can expose non-idempotent `CREATE TABLE`, upgrade, or options behavior without adding long-running performance infrastructure.

**Alternatives considered**:

- Benchmark harness: rejected because this slice is correctness hardening and benchmarks add operational/test infrastructure.
- Stress loop with high iteration counts: rejected because it increases flakiness and runtime without changing the contract under test.

## Decision: Assert Final State, Not Timing

**Decision**: Concurrent startup tests must verify final database state and subsequent provider usability instead of relying on interleaving or timing assertions.

**Rationale**: Timing assertions are brittle. The observable contract is that startup completes and the resulting database remains usable with the expected tenant-aware shape.

**Alternatives considered**:

- Assert specific execution ordering: rejected because provider startup order is intentionally not part of the public contract.
- Assert elapsed-time thresholds: rejected because local and CI environments vary.

## Decision: Cover Fresh and Existing Databases Separately

**Decision**: Add one scenario for an empty database path and one scenario for an already-seeded tenant-aware database.

**Rationale**: Fresh startup exercises concurrent schema creation. Existing startup exercises restart safety, preservation of persisted state, and metadata stability.

**Alternatives considered**:

- Only fresh startup: rejected because it misses production restart risk.
- Only existing startup: rejected because it misses all-table creation races.

## Decision: Preserve No-Schema Passive Behavior

**Decision**: Include concurrent construction with `InitializeSchema = false` and assert no database file is created.

**Rationale**: No-schema mode is an explicit operational contract. Concurrency hardening must not accidentally make passive services perform startup writes.

**Alternatives considered**:

- Treat no-schema mode as covered by previous idempotency tests: rejected because this slice specifically adds concurrent construction coverage.

## Decision: Production Changes Only For Demonstrated Defects

**Decision**: Start with tests. If a concurrency defect appears, fix the smallest SQLite schema/provider code path necessary while preserving the existing storage shape and public surface.

**Rationale**: Current schema initialization already uses idempotent DDL patterns. Adding locks, migrations, registries, or orchestration without a failing scenario would violate simplicity and explicitness.

**Alternatives considered**:

- Add a schema lock proactively: rejected unless tests prove current SQLite initialization is unsafe.
- Add a migration framework: rejected because the project explicitly avoids broad migration infrastructure in this phase.
