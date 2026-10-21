# Aster benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) harness for core operations across providers (#103).

## Run

From the repository root (BenchmarkDotNet locates the project from the working directory):

```bash
dotnet run -c Release --project bench/Aster.Benchmarks -- --filter '*'
```

- **Data size:** `ASTER_BENCH_VERSIONS` is a comma-separated list of total seeded resource versions: 100 authors plus a whole number of 4-version articles, so 100 plus a multiple of 4. Other totals are rejected. The default is `10000`. Example: `ASTER_BENCH_VERSIONS=10000,100000`.
- **Job:** the default is `short` (3 warmups, 3 iterations). Pass `--job default` for publication-grade statistics, or `--job dry` to smoke-test every scenario.
- **Scope:** narrow a run with `--filter '*Query*'`, `--filter '*Resource*'`, `--filter '*Portability*'` or `--filter '*VersionHistory*'`.
- **Storage report:** `dotnet run -c Release --project bench/Aster.Benchmarks -- storage 100,1000` prints a Markdown table, then exits. It reports SQLite bytes per stored version against the payload of two delta schemes, for the given history lengths. ADR 0005 uses it.
- **Output:** results land in `BenchmarkDotNet.Artifacts/` as GitHub Markdown and JSON.
- **CI:** the **Benchmarks** workflow runs the same command on demand and uploads the artifacts.

## What is measured

| Class | Scenarios |
|---|---|
| `ResourceBenchmarks` | create, update (append a version), activate, get latest, get active |
| `QueryBenchmarks` | metadata (`ResourceId`), scalar facet `Equals` and `Range`, array facet `ArrayContains`, reverse reference lookup; latest versions, pages of 50 |
| `PortabilityBenchmarks` | export of 100 resources (all versions), import preview of that snapshot into the same store |
| `VersionHistoryBenchmarks` | get latest, get oldest version, get all versions, version history, update; 20 resources with 100 or 1,000 versions each (own data set, see below) |

Each run covers both providers: in-memory, and SQLite on a temporary file. Postgres joins when epic #65 lands.

The seeded data set:
- 100 `Author` resources.
- `Article` resources with 4 versions each, adding up to the requested total. Each version carries about 3 KB of aspect payload: string, int and `string[]` facets, a long body, and an `Author` reference.
- Every other article is active in `published`.

`VersionHistoryBenchmarks` seeds its own data: 20 `Page` resources, each with 100 or 1,000 versions of about 2.6 KB. The edits follow a control-plane pattern:
- the title changes on every version
- the status changes every 5th version
- one body sentence changes every 10th version
- the owner changes every 50th version

Seeding writes through `IResourceVersionWriter`, so setup costs are excluded. Write benchmarks add rows as they run.

Baselines and their interpretation live in [`docs/performance/`](../docs/performance/).
