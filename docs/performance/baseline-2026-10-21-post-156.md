# Baseline: 2026-10-21 after #156, 10k versions

Re-run of the [first baseline](baseline-2026-10-21.md) after #156 bounded single-resource reads to the requested resource (#154).

## Setup

- **Commit:** `24c873a51eac1ddfbf8a978145de5a8c3ae190a2`: the #103 harness on `main` after #156 and #147.
- **Raw report:** [BenchmarkDotNet JSON](raw/2026-10-21-post-156-report-brief.json) and [Markdown](raw/2026-10-21-post-156-report-github.md).
- **Command, job, hardware, runtime and data:** the same as the [first baseline](baseline-2026-10-21.md#setup).

## Results

| Scenario | In-memory mean | In-memory alloc | SQLite mean | SQLite alloc | SQLite before #156 |
|---|---:|---:|---:|---:|---:|
| Create | 25 µs | 11 KB | 5.6 ms | 43 KB | 298 ms / 113 MB |
| Update (append a version) | 41 µs | 20 KB | 4.8 ms | 140 KB | 497 ms / 225 MB |
| Activate | 6.4 µs | 2.8 KB | 515 µs | 54 KB | 279 ms / 113 MB |
| Get latest | 2.5 µs | 1.6 KB | 75 µs | 51 KB | 249 ms / 113 MB |
| Get active | 2.6 µs | 1.6 KB | 92 µs | 56 KB | 266 ms / 115 MB |
| Query: metadata (`ResourceId`) | 0.64 ms | 182 KB | 10.0 ms | 3.2 MB | 10.4 ms |
| Query: scalar facet (`Equals`) | 0.31 ms | 66 KB | 15.7 ms | 769 KB | 15.7 ms |
| Query: scalar facet (`Range`) | 0.34 ms | 66 KB | 25.3 ms | 605 KB | 26.8 ms |
| Query: array facet (`ArrayContains`) | 2.21 ms | 649 KB | 138 ms | 4.8 MB | 138 ms |
| Reverse reference lookup | 1.38 ms | 349 KB | 157 ms | 6.3 MB | 135 ms |
| Export (100 resources) | 0.82 ms | 323 KB | 6.3 ms | 5.0 MB | 5.8 ms |
| Import preview (100 resources) | 6.50 ms | 5.0 MB | 144 ms | 9.9 MB | 144 ms |

## Findings

1. **#154 is fixed.** Single-resource operations no longer scale with tenant size.
   - SQLite get-latest drops from 249 ms to 75 µs (3,300×), and its allocation from 113 MB to 51 KB.
   - SQLite writes now cost about 5 ms, dominated by the commit rather than by reading the store.
   - The in-memory provider improves by 50–500× for the same reason.
   - The 100k and 1M runs are now meaningful for these rows.
2. **Queries and export are unchanged**, as expected: they never went through the single-resource path. The reverse lookup moved from 135 ms to 157 ms. That is within ShortRun noise for a 3-iteration job, but re-check it at 100k.
3. **SQLite import preview is still 144 ms (23× export).** The first baseline guessed it shared #154's unbounded read. This run rules that out. The cost has not been profiled yet. Candidates are the target-state read (`ReadTargetStateAsync`) and the per-resource validation in `BuildImportPlanAsync`. It is the next SQLite hotspot to investigate, after array queries and reverse lookups (finding 2 of the first baseline).
