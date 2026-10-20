# Aster Execution Roadmap

Last updated: 2026-10-20

This document records the implementation trail already cut through the repo. **Forward planning now lives in the [Aster 1.0 program](https://github.com/valence-works/aster/issues/58)**, a hierarchy of GitHub issues (program → epics → tasks) where each task maps to roughly one Spec Kit slice and one PR. The broader architecture narrative remains in `docs/Roadmap.md`.

## Current Position

Slices 001–038 have landed, along with the Aster Studio reference host. Aster has a working core SDK, in-memory runtime, SQLite JSON persistence and querying, query capability discovery and validation, provider conformance support, explicit index projection contracts, definition schema upgrades, portability primitives, host lifecycle hooks, tenant scoping, policies and lifecycle markers, version history inspection, historical activation, and facet validation/UI metadata.

The **summary series is closed.** Slices 022, 024, and 026–035 added pure summary views over result objects. No further summary, reporting, or hardening slices are planned unless a named consumer scenario or a measured problem justifies them (see the program's principles).

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
| `037-sqlite-startup-concurrency` | Landed | Bounded concurrency coverage for SQLite JSON schema/provider initialization across fresh databases, existing tenant-aware databases, and passive `InitializeSchema=false` construction. |
| `038-facet-metadata` | Landed | Optional declarative facet metadata (label, description, display order, default, allowed values, length, range, pattern, UI hint) with registration-time self-validation, save-time value validation, stable diagnostic codes, portability, and in-memory/SQLite parity. |
| Aster Studio (PR #57) | Landed | Reference host (`src/apps/Aster.AdminDemo`, ASP.NET Core + React) that generates editing forms from facet metadata and demonstrates versioning, channel activation, and field-level validation diagnostics. |

## What's Next

See the [Aster 1.0 program](https://github.com/valence-works/aster/issues/58) for phases, epics, tasks, dependencies, exit criteria, and explicitly deferred work. Do not plan new slices in this file; add or update issues in the program instead.

## Guardrails

- Prefer the simplest architecture that satisfies the current slice.
- Keep provider behavior explicit through capabilities and validation.
- Keep abstractions earned by demonstrated repetition or product need.
- Prefer composition and small helpers over framework-shaped extension points.
- Preserve provider agnosticism in core and provider specificity in provider packages.
- Keep local development and debugging straightforward.
- Every new slice names a consumer scenario (an Elsa spike finding, an Aster Studio screen, a reference sample, or an external adopter).
