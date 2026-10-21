# Performance guidance

What to expect from each provider, sized by measurement rather than by guesswork. The numbers come from the [post-#169 baseline](baseline-2026-10-21-post-169.md) and the [version-history runs in ADR 0005](../adr/0005-version-storage.md).
- **Hardware:** a 4-core cloud VM, .NET 10, BenchmarkDotNet `ShortRun`.
- **Reading them:** use them as orders of magnitude, not guarantees. Re-measure on your own hardware with the [harness](../../bench/README.md).

**Sizes are in resources (latest versions), not versions,** because queries run over latest versions. The baseline's 10k / 100k / 1M versions are about 2.5k / 25k / 250k resources at 4 versions each.

## What does not depend on volume

These operations stay flat from 2.5k to 250k resources (10k to 1M versions) on both providers:
- create, update, activate, get latest, get active
- get a specific version

| | In-memory | SQLite |
|---|---:|---:|
| Read one version (latest, active or by number) | 1–3 µs (by number: up to about 40 µs at 1,000 versions per resource) | 30–70 µs |
| Write (create, update) | 25–35 µs | 0.75–0.9 ms |
| Activate | 6–10 µs | 0.3–0.4 ms |

The SQLite write figure is single-threaded with a commit per write: about 1,000 writes per second. That suits control-plane data (ADR 0001: minutes to months between versions per resource).

## What does depend on volume

Facet queries filter every latest version in the tenant. No provider indexes facet values yet (#100).

| Query (SQLite, 50-row page) | 2.5k resources | 25k resources |
|---|---:|---:|
| Scalar facet `Equals` / `Range` | 16–28 ms | 53–65 ms |
| Array facet `ArrayContains` | 132 ms | 178 ms |
| Reverse reference lookup | 140 ms | 328 ms |
| Metadata `ResourceId` (#171) | 11 ms | 98 ms |
| Import preview, 100 resources (#171) | 148 ms | 1.2 s |

Version history:
- **Storage:** about 4.2 KB per version on SQLite (ADR 0005), so 1M versions is about 4 GB.
- **History reads:** reading one resource's full history costs about 12 ms per 1,000 versions on SQLite.

## Recommended volumes

| Provider | Use it for | Facet-query comfort zone | Beyond that |
|---|---|---|---|
| **In-memory** | Tests, demos, development | Up to about 25k resources: queries take 20–50 ms. | It holds everything in process memory and evaluates every candidate. Use SQLite or Postgres. |
| **SQLite** | Single-node hosts, small deployments, local development | **Up to about 25k resources per tenant** for interactive scalar facet queries (under about 100 ms). Array and reference queries already take 130–330 ms in that range, so use them for admin views and background work, not for request paths. | Move to Postgres with declared index projections (#97–#100), or narrow queries by metadata first. |
| **PostgreSQL** (planned, #65) | Production | Not measured yet. The expectation is that declared index projections (#100) turn facet scans into index seeks. | Re-baseline once #97–#100 land, before relying on any capacity figure. |

ADR 0001's interim estimate was tens of thousands of resources per definition on SQLite. The measurements confirm the magnitude but correct the unit: facet queries scan every latest version in the tenant, so the budget is about 25k resources **per tenant**, across all definitions. That Postgres with index projections handles an order of magnitude more is still an expectation, not a measurement, until #97–#100 can be benchmarked.

**Latency rule, unchanged from ADR 0001:** keep Aster off per-execution hot paths. A local read is fast, but behind HTTP (`Aster.AspNetCore`) every read adds a network round trip. Resolve the version when it changes (lifecycle hooks) and cache it in the host.

## Version growth and pruning

Snapshots stay (ADR 0005). Apply a version-pruning policy (016, applied through 019) when either holds:
- **A resource's history passes about 1,000 versions.** Full-history reads (history views, export with all versions) cross about 10 ms on SQLite there and grow linearly.
- **A tenant's version storage outgrows its budget.** Budget about 4 KB per version for roughly 3 KB of aspect payload; your ratio depends on payload size.

Single-version reads and writes don't slow down as history grows (#166), so pruning is about storage and history views, not about everyday operations.

## Decision: no generated-column indexes for SQLite now

SQLite can index JSON paths through generated columns or expression indexes. **We won't add them now:**
- **Scalar queries are already fast enough:** at about 25k resources they take 50–65 ms, which is acceptable for SQLite's role.
- **Array and reference queries wouldn't benefit:** they cost the most, but they enumerate `json_each`, which a single-column index doesn't help.
- **Every declared projection would need a schema migration** in a provider that deliberately has no general migration framework (016).

Revisit if a host needs SQLite beyond about 25k resources per definition with interactive facet queries. The design would mirror ADR 0004: materialize declared projections into an indexed side table at write time, rather than adding per-path columns. This feeds the index work in epic #65 (#100).

## Known outliers

Issue #171 tracks two SQLite paths that should be bounded by their input but still grow with the tenant:
- import preview
- the `ResourceId` metadata query

Import builds the same plan as its preview, so until #171 is fixed, expect both to take seconds on tenants of 25k+ resources.
