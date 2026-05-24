# Aster Execution Roadmap

Last updated: 2026-05-24

This roadmap tracks the implementation trail we have already cut through the repo and the next product slices that should stay small enough for Spec Kit-driven PRs. It is intentionally execution-oriented; the broader architecture narrative remains in `docs/Roadmap.md` and the wiki.

## Current Position

Aster now has a working Core SDK, in-memory runtime, SQLite JSON persistence/querying, provider capability discovery, provider validation alignment, provider authoring ergonomics, a reusable provider conformance harness, SQLite facet sorting, portable operator expansion, SQLite date-like ranges, explicit provider-declared index projections, explicit definition schema upgrade behavior, deterministic portability primitives, and explicit host lifecycle hooks.

The Phase 4 core workstream is complete enough to defer optional recipes. The active workstream is now **Phase 5: Multi-tenancy, Policies, Advanced Versioning**, starting with explicit tenant-aware boundaries before policies or cross-tenant behavior are introduced.

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
| `012-definition-schema-upgrades` | Landed | Explicit resource definition lineage, schema status inspection, append-only schema upgrades, and carried-forward data diagnostics. |
| `013-portability-primitives` | Landed | Deterministic export/import primitives for definitions, resources, resource versions, activation state, strict validation, previews, identity mapping, remapping, and provider-backed atomic writes. |
| `014-host-lifecycle-hooks` | Landed | Explicit before/after hooks for save, activation, deactivation, schema upgrades, export, preview import, and write import with deterministic ordering and fail-closed diagnostics. |

## Near-Term Roadmap

### 015 — Tenant Scoping

**Goal:** Add explicit tenant-aware boundaries for definitions, resources, activation state, queries, schema upgrades, portability, and lifecycle hooks while preserving default single-tenant behavior.

Candidate scope:

- Stable tenant identity and default single-tenant scope.
- Scoped definition/resource lifecycle, activation state, queries, schema upgrades, portability snapshots, and lifecycle hook contexts.
- Fail-closed diagnostics for missing, invalid, ambiguous, or mismatched tenant scope.
- Keep shared-definition inheritance, cross-tenant queries, authorization, policy engines, migrations, runtime scanning, provider registries, public SQL, and `IQueryable<Resource>` out of scope.

## Later Roadmap

| Area | Intent |
|---|---|
| Optional recipes | Separate `Aster.Recipes` package for hosts that want recipe execution over portability primitives. |
| Multi-tenancy extensions | Shared definitions, tenant hierarchy, and cross-tenant administrative workflows after explicit tenant boundaries exist. |
| Policies | Retention, archival, soft-delete, and pruning policies. |
| Operational hardening | Benchmarks, migration idempotency, concurrency hardening, and large-data test suites. |

## Guardrails

- Prefer the simplest architecture that satisfies the current slice.
- Keep provider behavior explicit through capabilities and validation.
- Keep abstractions earned by demonstrated repetition or product need.
- Prefer composition and small helpers over framework-shaped extension points.
- Preserve provider agnosticism in core and provider specificity in provider packages.
- Keep local development and debugging straightforward.
