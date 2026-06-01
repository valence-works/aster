# Aster Execution Roadmap

Last updated: 2026-06-01

This roadmap tracks the implementation trail we have already cut through the repo and the next product slices that should stay small enough for Spec Kit-driven PRs. It is intentionally execution-oriented; the broader architecture narrative remains in `docs/Roadmap.md` and the wiki.

## Current Position

Aster now has a working Core SDK, in-memory runtime, SQLite JSON persistence/querying, provider capability discovery, provider validation alignment, provider authoring ergonomics, a reusable provider conformance harness, SQLite facet sorting, portable operator expansion, SQLite date-like ranges, explicit provider-declared index projections, explicit definition schema upgrade behavior, deterministic portability primitives, explicit host lifecycle hooks, explicit tenant scoping, policy foundations, host-controlled policy application orchestration, host-controlled lifecycle restore workflows, host-controlled policy pruning application, read-only version history inspection, explicit historical version activation, policy application summaries, batch version history inspection, version history summaries, operational hardening coverage, lifecycle restore summaries, policy preview summaries, portability result summaries, schema upgrade summaries, query validation summaries, index projection summaries, policy validation summaries, lifecycle hook outcome summaries, portable validation summaries, lifecycle marker result summaries, and SQLite schema idempotency hardening.

The Phase 4 core workstream is complete enough to defer optional recipes. The active workstream is now **Phase 5: Multi-tenancy, Policies, Advanced Versioning** with small cross-cutting host-reporting and operational-hardening follow-ups. Explicit tenant-aware boundaries, policy foundations, policy application orchestration, reversible lifecycle restore, destructive pruning application, version history inspection, historical version activation, policy application summaries, batch version history inspection, version history summaries, operational hardening, lifecycle restore summaries, policy preview summaries, portability result summaries, schema upgrade summaries, query validation summaries, index projection summaries, policy validation summaries, lifecycle hook outcome summaries, portable validation summaries, lifecycle marker result summaries, and SQLite schema idempotency hardening have landed; SQLite startup concurrency hardening is the current bounded operational slice.

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
| `021-historical-version-activation` | Landed | Explicit activation of any existing historical or latest resource version while preserving immutable versions, latest identity, activation state separation, tenant scoping, lifecycle hooks, and in-memory/SQLite parity. |
| `022-policy-application-summaries` | Landed | Pure host-facing summaries for policy application and pruning application results with deterministic status counts, completion booleans, affected target counts, diagnostic counts, and no storage or reporting framework. |
| `023-batch-version-history` | Landed | Explicit batch version history inspection for selected resource IDs with first-seen distinct ordering, tenant scoping, missing-resource histories, SQLite parity, and no storage or query-surface expansion. |
| `024-version-history-summaries` | Landed | Pure host-facing summaries for single and batch version history results with deterministic version-state counts, selected/missing resource counts, lifecycle state counts, and no storage, provider, service, query planner, policy evaluation, or reporting infrastructure. |
| `025-operational-hardening` | Landed | Bounded retry and concurrency-sensitive coverage for lifecycle restore, policy pruning retries, SQLite persisted pruning retries, and repeated historical activation without new product APIs, storage schema changes, schedulers, benchmark infrastructure, or dependencies. |
| `026-lifecycle-restore-summaries` | Landed | Pure host-facing lifecycle restore preview and application summaries with deterministic status counts, affected resource counts, diagnostic counts, completion booleans, and no storage, provider, service registration, audit persistence, or mutation behavior. |
| `027-policy-preview-summaries` | Landed | Pure host-facing policy preview summaries with deterministic candidate, outcome, policy-kind, distinct resource, distinct resource-version target, and diagnostic counts without storage, provider, service registration, audit persistence, or mutation behavior. |
| `028-portability-result-summaries` | Landed | Pure host-facing portability export/import preview/import summaries with deterministic entity counts, identity mapping reason counts, diagnostic counts, status booleans, and no storage, provider, service registration, recipe package, audit persistence, or mutation behavior. |
| `029-schema-upgrade-summaries` | Landed | Pure host-facing schema status and schema upgrade summaries with deterministic status, upgrade-needed, blocking, unknown-lineage, upgraded-resource, carried-forward aspect-key, and definition-version counts without storage, provider, service registration, audit persistence, or mutation behavior. |
| `030-query-validation-summaries` | Landed | Pure host-facing query validation summaries with deterministic failure-code, path, feature, total failure, and validity counts without storage, provider, service registration, query planner, execution behavior, public SQL, or public `IQueryable<Resource>`. |
| `031-index-projection-summaries` | Landed | Pure host-facing index projection validation/evaluation summaries with deterministic failure-code, failure-field, failure-source, value-field-type, value-field, total value, total failure, and validity counts without storage, provider, service registration, query planner, execution behavior, public SQL, public `IQueryable<Resource>`, or mutation behavior. |
| `032-policy-validation-summaries` | Landed | Pure host-facing policy validation summaries with deterministic diagnostic-code, diagnostic-path, policy-id, resource-id, resource-version, total diagnostic, and validity counts without storage, provider, service registration, scheduler, audit persistence, policy execution, policy validation behavior, public SQL, public `IQueryable<Resource>`, or mutation behavior. |
| `033-lifecycle-hook-outcome-summaries` | Landed | Pure host-facing lifecycle hook outcome summaries with deterministic status, outcome-code, diagnostic-code, lifecycle-point, hook-type, total outcome, total diagnostic, success, and failure counts without storage, provider, service registration, scheduler, audit persistence, lifecycle dispatcher behavior, hook execution behavior, public SQL, public `IQueryable<Resource>`, or mutation behavior. |
| `034-portable-validation-summaries` | Landed | Pure host-facing portable snapshot validation summaries with deterministic validity, error, total diagnostic, diagnostic severity, and diagnostic code counts without storage, provider, service registration, reporting framework, import/export behavior, validation behavior, public SQL, public `IQueryable<Resource>`, or mutation behavior. |
| `035-lifecycle-marker-result-summaries` | Landed | Pure host-facing lifecycle marker result summaries with deterministic success, failure, marker state, marker resource, diagnostic code, diagnostic path, diagnostic resource, total result, and total diagnostic counts without storage, provider, service registration, reporting framework, lifecycle marker service behavior, marker store behavior, public SQL, public `IQueryable<Resource>`, or mutation behavior. |
| `036-sqlite-schema-idempotency` | Landed | SQLite JSON schema initialization idempotency coverage for repeated provider initialization, legacy pre-tenant upgrade reruns, and `InitializeSchema=false` compatibility without product APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, schedulers, benchmark infrastructure, or dependencies. |

## Near-Term Roadmap

### 037 — SQLite Startup Concurrency Hardening

**Status:** In progress on `037-sqlite-startup-concurrency`.

**Goal:** Add bounded operational hardening coverage for concurrent SQLite JSON schema/provider initialization.

Scope:

- Verify concurrent fresh-database startup completes and leaves the schema usable.
- Verify concurrent startup against an existing tenant-aware database preserves persisted data and table shape.
- Verify concurrent `InitializeSchema=false` construction remains passive.
- Preserve existing provider behavior, storage format, service registration, public SQL, public `IQueryable<Resource>`, query planner behavior, schedulers, benchmark infrastructure, and dependencies.

### 038 — Next Bounded Slice

**Goal:** Choose one small continuation slice after SQLite startup concurrency hardening lands.

Candidate scope:

- Advanced versioning follow-up only if it stays explicit, append-only, and provider-agnostic.
- A deliberately bounded policy or lifecycle follow-up, such as reporting or audit-oriented operation views, only after a separate spec defines exact host value.
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
