# Aster Execution Roadmap

Last updated: 2026-05-18

This roadmap tracks the implementation trail we have already cut through the repo and the next product slices that should stay small enough for Spec Kit-driven PRs. It is intentionally execution-oriented; the broader architecture narrative remains in `docs/Roadmap.md` and the wiki.

## Current Position

Aster now has a working Core SDK, in-memory runtime, SQLite JSON persistence/querying, provider capability discovery, provider validation alignment, provider authoring ergonomics, a reusable provider conformance harness, SQLite facet sorting, portable operator expansion, SQLite date-like ranges, and explicit provider-declared index projections.

The active workstream is **Phase 3: Query Capabilities & Typed Querying**. The next useful slice is definition schema versioning and explicit upgrade behavior, still without introducing migrations, planner behavior, runtime scanning, provider registries, public SQL, or `IQueryable<Resource>`.

## Landed Slices

| Slice | Status | What It Established |
|---|---|---|
| `001-core-sdk-foundation` | Landed | Core domain contracts, resource definitions, immutable resource versions, activation state, in-memory stores, typed aspect binding, initial query AST, and workbench endpoints. |
| `002-sqlite-json-querying` | Landed | SQLite JSON resource persistence and provider-backed query execution over metadata, aspect presence, scalar facets, paging, and sorting. |
| `003-query-capabilities-typed` | Landed | Provider capability descriptions, query preflight validation, provider-agnostic query semantics, and initial typed facet filter helpers. |
| `004-provider-validation-execution` | Landed | Shared validation before execution, fail-closed missing capability behavior, and consistent unsupported-query exceptions. |
| `005-provider-authoring-ergonomics` | Landed | Explicit DI helper for custom query providers and provider authoring documentation. |
| `006-provider-conformance-tests` | Landed | Reusable conformance tests that compare declared capabilities with validation and execution behavior. |
| `007-sqlite-facet-sorting` | Landed | SQLite facet sorting, null-last behavior, numeric/text ordering, capability updates, and tests/docs. |
| `008-typed-query-authoring` | Landed | Typed facet sort helpers, small logical composition helpers, and updated roadmap/query docs over the existing query AST. |
| `009-portable-operators` | Landed | Portable `NotEquals`, `In`, `StartsWith`, and facet `Exists` operators with provider capabilities, validation, built-in execution, typed helpers, and conformance coverage. |
| `010-sqlite-date-ranges` | Landed | SQLite date-like facet ranges for accepted JSON string date/time values with capability, validation, conformance, and docs coverage. |
| `011-explicit-indexing-model` | Landed | Provider-declared index projection contracts, validation, evaluation, capability discovery, and provider-authoring docs without physical indexes or query planning. |

## Near-Term Roadmap

### 012 — Definition Schema Versions & Upgrade Flow

**Goal:** Make the definition-version story explicit enough for long-lived resources.

Candidate scope:

- Model `ResourceDefinitionVersion` references on resources.
- Define upgrade behavior from older definition versions.
- Add validation and tests for resources that span schema versions.

### 013 — Portability Primitives

**Goal:** Start Phase 4 with deterministic export/import primitives for definitions and resources.

Candidate scope:

- Export definition and resource snapshots with stable references.
- Import with deterministic ID remapping and collision diagnostics.
- Keep recipes and host lifecycle hooks separate follow-up slices.

## Later Roadmap

| Area | Intent |
|---|---|
| Portability | Export/import definitions and resources with deterministic ID remapping. |
| Host hooks | Lifecycle hooks around save, activation, deactivation, and import/export. |
| Multi-tenancy | Tenant-aware definition scope and query boundaries. |
| Policies | Retention, archival, soft-delete, and pruning policies. |
| Operational hardening | Benchmarks, migration idempotency, concurrency hardening, and large-data test suites. |

## Guardrails

- Prefer the simplest architecture that satisfies the current slice.
- Keep provider behavior explicit through capabilities and validation.
- Keep abstractions earned by demonstrated repetition or product need.
- Prefer composition and small helpers over framework-shaped extension points.
- Preserve provider agnosticism in core and provider specificity in provider packages.
- Keep local development and debugging straightforward.
