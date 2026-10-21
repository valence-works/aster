# ADR 0005: Version storage: full snapshots

- **Status:** Accepted
- **Date:** 2026-10-21
- **Program task:** [#105](https://github.com/valence-works/aster/issues/105) (epic [#66](https://github.com/valence-works/aster/issues/66))
- **Inputs:**
  - the roadmap's ADR candidate 4 ("start with snapshots; add deltas and compaction later if needed")
  - the benchmark harness (#103)
  - policy pruning (016, 019)
  - historical activation (021)

## Context

Every resource version is stored as a full JSON snapshot of its aspects. SQLite keeps one row per `(tenant_id, resource_id, version)`. The alternative is to store each version as a delta from its predecessor. This ADR decides whether to switch now, and records when to look again.

### Measurements

Two harness additions from this task:
- `VersionHistoryBenchmarks`: 20 `Page` resources with 100 or 1,000 versions each.
- The `storage` report, which prints the table below.

Each version is about 2.6 KB of aspect payload. The edits follow a control-plane pattern:
- the title changes on every version
- the status changes every 5th version
- one body sentence changes every 10th version
- the owner changes every 50th version

Setup: cloud VM, Xeon 2.80 GHz, 4 cores, .NET 10.0.13, `ShortRun`. The raw reports are in [`docs/performance/raw/`](../performance/raw/) (`2026-10-21-version-history-*`).

**Storage** (`dotnet run -c Release --project bench/Aster.Benchmarks -- storage 100,1000`):

| Versions per resource | Stored versions | SQLite file | Bytes per version | Snapshot payload | Changed-aspect delta | Changed-facet delta |
|---:|---:|---:|---:|---:|---:|---:|
| 100 | 2,000 | 8.0 MB | 4,182 B | 2,576 B | 2,534 B | 318 B |
| 1,000 | 20,000 | 79.4 MB | 4,161 B | 2,578 B | 2,536 B | 296 B |

- **SQLite file:** the file size after a WAL checkpoint.
- **Delta columns:** the average serialized payload per version that a delta scheme would store instead of a snapshot. "Changed aspect" stores every aspect that changed, in full. "Changed facet" stores only the changed facets.

**Read and write latency** (`--filter '*VersionHistory*'`):

| Operation | In-memory, 100 | In-memory, 1,000 | SQLite, 100 | SQLite, 1,000 |
|---|---:|---:|---:|---:|
| Get latest | 9.6 µs | 263 µs | 1.14 ms | 11.4 ms |
| Get oldest version | 8.6 µs | 262 µs | 1.17 ms | 12.4 ms |
| Get all versions | 8.3 µs | 256 µs | 1.12 ms | 11.8 ms |
| Version history | 19 µs | 515 µs | 1.24 ms | 12.6 ms |
| Update (get latest, then append) | 545 µs | 619 µs | 4.2 ms | 27.4 ms |

### What the numbers say

1. **Storage is linear and modest:** about 4.2 KB per version on SQLite, flat from 100 to 1,000 versions per resource.
   - 1M stored versions come to about 4 GB.
   - About 1.6 KB of each row is not aspect payload: the resource envelope (IDs, definition, timestamps, hash) plus SQLite row and index overhead. A delta row keeps all of that.
2. **Aspect-level deltas save almost nothing (2%)** for this edit pattern, because the facet that changes on every version (the title) shares an aspect with the large body. Aspects group facets by meaning, not by how often they change, so this is the common case.
3. **Facet-level deltas cut the payload 8.7×, but the file only about 2.2×.** The estimate is 4,182 − 2,578 + 318 ≈ 1.9 KB per version, because the per-row overhead stays.
4. **Read latency grows with history length, but not because of snapshots.** Get latest and get version cost the same as reading all versions. The cause is that `DefaultResourceManager` loads the resource's whole history and picks one row (#166). With keyed reads, a snapshot store answers both from one row whatever the history length. A delta store cannot: it has to replay a chain or maintain checkpoints.

## Decision

**Keep full snapshots.** No delta storage, no compaction and no storage format change.

1. **Growth control is pruning.** Version-pruning policies (016, applied through 019) bound history per resource. Hosts that keep everything accept about 4 KB per version for this edit pattern.
2. **Read cost is fixed in the read path, not the storage format.** #166 makes single-version operations keyed reads: get latest, get version, update, activate and deactivate.
3. **The first storage lever, if one is ever needed, is payload compression,** not deltas. It is per row, keeps every version self-contained, and hides behind the provider. It has not been measured: the benchmark body text is synthetic and too repetitive to give an honest ratio.

### Rejected alternatives

- **Aspect-level deltas:** a 2% saving for the measured pattern (finding 2). Not worth a format change.
- **Facet-level deltas** (merge patches with periodic full checkpoints): about 2.2× smaller files, at the cost of:
  - **Reads:** any non-latest read replays a chain. Historical activation (021) makes any version potentially hot, and pruning would have to rebase chains.
  - **Queries:** the portable query AST runs over JSON paths in the stored payload (ADR 0002 §9, ADR 0004). Latest and active versions would still need full materialized copies, so the hot set saves nothing.
  - **Portability:** the snapshot format, export and import, and per-version hashes would all change.
  - **Providers:** the work is repeated in every provider: in-memory, SQLite, and the planned PostgreSQL and SQL Server providers.

  A 2× disk saving does not justify that.

## Revisit when

Reopen this decision only if one of these holds. Re-measure with the same commands first.

| Trigger | Threshold |
|---|---|
| Storage after pruning | A real tenant's `resource_versions` exceeds **10 GB** with pruning policies applied, or a host's retention rules forbid pruning and average history exceeds **5,000 versions per resource**. |
| Delta yield | The `storage` report on a host's real data shows **at least 4×** on-disk savings for facet deltas, so the payload dominates the per-row overhead. |
| Read latency after #166 | Get latest or get version on SQLite grows by **more than 2×** from 100 to 1,000 versions per resource, or reading one resource's full history exceeds **100 ms** at 10,000 versions. |
| Compression not enough | Payload compression has been tried and the storage trigger still holds. |

## Consequences

- No migration, provider or portability format change.
- #166 is a prerequisite for the latency claim in decision 2. Its acceptance criteria use `VersionHistoryBenchmarks`.
- `VersionHistoryBenchmarks` and the `storage` report stay in the harness, so the triggers can be checked with one command each.
