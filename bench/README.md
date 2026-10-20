# Aster benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) harness for core operations across providers (#103).

## Run

From the repository root (BenchmarkDotNet locates the project from the working directory):

```bash
dotnet run -c Release --project bench/Aster.Benchmarks -- --filter '*'
```

- **Data size:** `ASTER_BENCH_VERSIONS` is a comma-separated list of total seeded resource versions. The default is `10000`. Example: `ASTER_BENCH_VERSIONS=10000,100000`.
- **Job:** the default is `short` (3 warmups, 3 iterations). Pass `--job default` for publication-grade statistics, or `--job dry` to smoke-test every scenario.
- **Scope:** narrow a run with `--filter '*Query*'`, `--filter '*Resource*'` or `--filter '*Portability*'`.
- **Output:** results land in `BenchmarkDotNet.Artifacts/` as GitHub Markdown and JSON.
- **CI:** the **Benchmarks** workflow runs the same command on demand and uploads the artifacts.

## What is measured

| Class | Scenarios |
|---|---|
| `ResourceBenchmarks` | create, update (append a version), activate, get latest, get active |
| `QueryBenchmarks` | metadata (`ResourceId`), scalar facet `Equals` and `Range`, array facet `ArrayContains`, reverse reference lookup; latest versions, pages of 50 |
| `PortabilityBenchmarks` | export of 100 resources (all versions), import preview of that snapshot into the same store |

Each run covers both providers: in-memory, and SQLite on a temporary file. Postgres joins when epic #65 lands.

The seeded data set:
- 100 `Author` resources.
- `Article` resources with 4 versions each, adding up to the requested total. Each version carries about 3 KB of aspect payload: string, int and `string[]` facets, a long body, and an `Author` reference.
- Every other article is active in `published`.

Seeding writes through `IResourceVersionWriter`, so setup costs are excluded. Write benchmarks add rows as they run.

Baselines and their interpretation live in [`docs/performance/`](../docs/performance/).
