# Performance baselines

Baselines from the [benchmark harness](../../bench/README.md) (#103), part of epic #66. Each baseline records:
- the commit it was measured on
- the hardware and runtime
- the job settings, so later runs can be compared like for like
- the raw BenchmarkDotNet report, under [`raw/`](raw/), from the post-#156 baseline on

| Baseline | Data size | Providers | Notes |
|---|---|---|---|
| [2026-10-21](baseline-2026-10-21.md) | 10k versions | in-memory, SQLite | First baseline. Single-resource operations scale with tenant size (#154). |
| [2026-10-21 after #156](baseline-2026-10-21-post-156.md) | 10k versions | in-memory, SQLite | #154 fixed: SQLite get-latest 249 ms → 75 µs. Import preview is the next SQLite hotspot. |

## Comparing a run against a baseline

1. Run the same scenarios at the same data size from the repository root:

   ```bash
   ASTER_BENCH_VERSIONS=10000 dotnet run -c Release --project bench/Aster.Benchmarks -- --filter '*'
   ```

2. Compare `Mean` and `Allocated` per row with the baseline table.
   - **Hardware:** absolute times vary with it. Treat differences under about 20% on `--job short` as noise.
   - **Allocations:** they are much more stable than times, and are usually the clearer regression signal.
3. When a change moves a number on purpose, add a new baseline file rather than editing an old one. Then link it in the table above.
