# Aster Execution Roadmap

Last updated: 2026-05-29

This roadmap tracks the implementation trail we have already cut through the repo and the next product slices that should stay small enough for Spec Kit-driven PRs. It is intentionally execution-oriented; the broader architecture narrative remains in `docs/Roadmap.md` and the wiki.

## Current Position

Aster now has a working Core SDK, in-memory runtime, SQLite JSON persistence/querying, provider capability discovery, provider validation alignment, provider authoring ergonomics, a reusable provider conformance harness, SQLite facet sorting, portable operator expansion, SQLite date-like ranges, explicit provider-declared index projections, explicit definition schema upgrade behavior, deterministic portability primitives, explicit host lifecycle hooks, explicit tenant scoping, policy foundations, host-controlled policy application orchestration, host-controlled lifecycle restore workflows, host-controlled policy pruning application, and read-only version history inspection.

The Phase 4 core workstream is complete enough to defer optional recipes. The active workstream is now **Phase 5: Multi-tenancy, Policies, Advanced Versioning**. Explicit tenant-aware boundaries, policy foundations, policy application orchestration, reversible lifecycle restore, destructive pruning application, and version history inspection have landed; historical version activation is the current bounded advanced-versioning slice.

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
| `015-tenant-scoping` | Landed | Explicit tenant boundaries for definitions, resources, activation state, queries, schema upgrades, portability snapshots, and lifecycle hook contexts with default single-tenant compatibility. |
| `016-policy-foundations` | Landed | Definition-attached policy declarations, validation diagnostics, deterministic non-mutating previews, explicit archive/soft-delete lifecycle markers, lifecycle-state querying, and portability preservation. |
| `017-policy-application-orchestration` | Landed | Host-controlled application of selected archive/soft-delete preview outcomes with per-candidate results, stale/policy validation, tenant scoping, and bounded provider reads. |
| `018-lifecycle-restore-workflows` | Landed | Host-controlled preview and application for restoring archive/soft-delete markers with tenant scoping, idempotent already-restored outcomes, stable diagnostics, and no version or activation mutation. |
| `019-policy-pruning-application` | Landed | Host-controlled application of selected version-pruning preview outcomes with latest/active protection, policy revalidation, tenant scoping, deterministic retries, provider fallback diagnostics, and no schema or portability format changes. |
| `020-version-history-inspection` | Landed | Read-only host-facing version history inspection with latest/draft/active-channel summaries, lifecycle marker visibility, conservative maintenance hints, tenant scoping, and in-memory/SQLite parity without schema changes or query-surface expansion. |

## Near-Term Roadmap

### 021 — Historical Version Activation

**Status:** In progress on `021-historical-version-activation`.

**Goal:** Allow hosts to explicitly activate any existing resource version, including historical non-latest versions, through the existing activation APIs.

Scope:

- Remove the latest-only activation restriction while preserving version existence validation.
- Keep latest-version identity and immutable resource versions unchanged.
- Preserve single-active versus multi-active channel behavior.
- Preserve tenant scoping, lifecycle hooks, and provider-backed activation storage.
- Keep schema changes, automatic jobs, ambient authorization, provider registries, public SQL, public `IQueryable<Resource>`, and broad workflow/state-machine infrastructure out of scope.

### 022 — Next Bounded Phase 5 Slice

**Goal:** Choose one small continuation slice after historical activation lands.

Candidate scope:

- Advanced versioning follow-up only if it stays explicit, append-only, and provider-agnostic.
- A deliberately bounded policy follow-up, such as reporting or audit-oriented application summaries, only after a separate spec defines exact host value.
- Tenant extension behavior, such as shared definitions or administrative workflows, only if the boundaries stay explicit.
- Bounded operational hardening such as targeted benchmarks, migration idempotency checks, or concurrency stress tests.

## Later Roadmap

| Area | Intent |
|---|---|
| Optional recipes | Separate `Aster.Recipes` package for hosts that want recipe execution over portability primitives. |
| Multi-tenancy extensions | Shared definitions, tenant hierarchy, and cross-tenant administrative workflows after explicit tenant boundaries exist. |
| Policies | Optional policy reporting, audit surfaces, or recipe integration after host-controlled application primitives stabilize. |
| Operational hardening | Benchmarks, migration idempotency, concurrency hardening, and large-data test suites. |

## Guardrails

- Prefer the simplest architecture that satisfies the current slice.
- Keep provider behavior explicit through capabilities and validation.
- Keep abstractions earned by demonstrated repetition or product need.
- Prefer composition and small helpers over framework-shaped extension points.
- Preserve provider agnosticism in core and provider specificity in provider packages.
- Keep local development and debugging straightforward.
