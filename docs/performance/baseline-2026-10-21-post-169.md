# Baseline: 2026-10-21 after #169, 10k / 100k / 1M versions

The first baseline at more than one data size. It was measured after these fixes:
- #156: single-resource operations no longer read the whole tenant.
- #166: single-version operations no longer read the whole resource history.
- #169: SQLite latest-by-ID reads seek by key instead of walking the tenant.

## Setup

- **Commit:** `be434c6423a33d8752f1545f348ea57d63c6cbe3`: the #103 harness on `main` at #170.
- **Commands:**
  - `ASTER_BENCH_VERSIONS=10000,100000 dotnet run -c Release --project bench/Aster.Benchmarks -- --filter '*'`
  - `ASTER_BENCH_VERSIONS=1000000 ... --filter '*ResourceBenchmarks*'`
- **Job:** `ShortRun` (3 warmups, 3 iterations). Treat differences under about 20% as noise.
- **Hardware and runtime:** the same as the [first baseline](baseline-2026-10-21.md#setup).
- **Data:** at each size, 100 authors plus articles with 4 versions each, about 3 KB per version. Half the articles are active in `published`.
- **SQLite:** the seeding WAL is checkpointed before measuring.
- **Raw reports:** [JSON](raw/2026-10-21-post-169-report-brief.json) and [Markdown](raw/2026-10-21-post-169-report-github.md) for 10k and 100k; 1M ([JSON](raw/2026-10-21-post-169-1m-report-brief.json), [Markdown](raw/2026-10-21-post-169-1m-report-github.md)).

## Single-resource operations

| Scenario | In-memory 10k | In-memory 100k | In-memory 1M | SQLite 10k | SQLite 100k | SQLite 1M |
|---|---:|---:|---:|---:|---:|---:|
| Create | 24 µs | 28 µs | 24 µs | 763 µs | 779 µs | 792 µs |
| Update (append a version) | 24 µs | 32 µs | 34 µs | 835 µs | 772 µs | 881 µs |
| Activate | 6.0 µs | 9.2 µs | 9.9 µs | 326 µs | 390 µs | 408 µs |
| Get latest | 1.3 µs | 2.0 µs | 2.1 µs | 51 µs | 50 µs | 50 µs |
| Get active | 2.5 µs | 3.0 µs | 3.2 µs | 61 µs | 62 µs | 68 µs |

Allocations are flat across sizes: on SQLite, 42 KB for create and 16 KB for get latest.

## Queries and portability

| Scenario | In-memory 10k | In-memory 100k | SQLite 10k | SQLite 100k |
|---|---:|---:|---:|---:|
| Query: metadata (`ResourceId`) | 0.75 ms | 34.6 ms | 10.7 ms | 98.4 ms |
| Query: scalar facet (`Equals`) | 0.33 ms | 19.9 ms | 15.8 ms | 53.2 ms |
| Query: scalar facet (`Range`) | 0.39 ms | 20.9 ms | 28.3 ms | 65.4 ms |
| Query: array facet (`ArrayContains`) | 1.63 ms | 23.1 ms | 132 ms | 178 ms |
| Reverse reference lookup | 1.89 ms | 50.7 ms | 140 ms | 328 ms |
| Export (100 resources) | 0.89 ms | 13.0 ms | 6.0 ms | 6.0 ms |
| Import preview (100 resources) | 6.6 ms | 6.1 ms | 148 ms | 1,196 ms |

Queries and portability were not run at 1M. See finding 4.

## Findings

1. **Single-resource operations are flat from 10k to 1M** on both providers: SQLite get latest takes 51, 50 and 50 µs at 10k, 100k and 1M. This is the property the harness was built to prove (#103).
   - **Before #169:** SQLite create grew from 5.6 ms at 10k to 48 ms at 100k, because every latest-by-ID read walked the tenant. A WAL checkpoint after seeding was tried first and ruled out.
   - **SQLite writes now cost about 0.8 ms,** 7× faster than at 10k before the fix.
2. **Facet queries scan the tenant, as designed for now.** Nothing indexes facet values yet, so they are O(tenant):
   - SQLite: 3–4× from 10k to 100k for scalar filters, and 1.3–2.3× for array filters and reverse lookups, where per-row `json_each` dominates.
   - In-memory: 14–60×, because it evaluates every candidate.
   - **Remedy:** index projections (#100), and materialized `projection_values` for SQL Server (ADR 0004).
3. **Two SQLite paths that should be bounded by their input still grow with the tenant.** Tracked in #171.
   - **Import preview:** 148 ms → 1.2 s, with flat allocation.
   - **The `ResourceId` metadata query:** 10.7 → 98 ms, and its allocation grows 10× for one result.
4. **1M versions: single-resource operations were measured, queries and portability were not.**
   - The 1M rows above come from a separate `ResourceBenchmarks` run, which took 74 minutes, most of it seeding a million versions for each of the 10 cases (the same cases took 9 minutes at 100k).
   - Queries would only confirm finding 2 at 10× the cost. By the 10k → 100k slopes, a SQLite scalar query would take about 0.3–0.5 s at 1M and a reverse lookup more than 1 s.
   - Import preview would take more than 10 s until #171 is fixed.
   - Re-run them at 1M once #100 and #171 land; the harness needs only `ASTER_BENCH_VERSIONS=1000000`.
5. **In-memory export grows with the tenant** (0.89 → 13 ms for 100 resources), because it filters the whole store in memory. That is acceptable for a development provider and not tracked.
