# Implementation Plan: Tenant Scoping

**Branch**: `015-tenant-scoping` | **Date**: 2026-05-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/015-tenant-scoping/spec.md`

## Summary

Add explicit tenant-aware boundaries as the first Phase 5 slice. The plan introduces a small tenant scope model, threads optional tenant scope through request DTOs where they already exist, adds explicit tenant overloads for method-style APIs, partitions definition/resource/query/activation/schema/portability behavior by the effective tenant, and exposes the effective tenant to lifecycle hooks. Existing callers that omit tenant scope continue to use a documented default single-tenant scope. The slice intentionally avoids tenant hierarchy, shared-definition inheritance, cross-tenant queries, authorization, policy engines, migrations, runtime scanning, provider registries, public SQL, and public `IQueryable<Resource>`.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, in-memory store, SQLite JSON provider, resource manager/store abstractions, query capability/validation stack, portability service, lifecycle hook dispatcher, xUnit test stack; no new dependencies  
**Storage**: Existing resource definitions, resource versions, activation state, and portable snapshots extended with tenant scope metadata; SQLite initialization may perform minimal idempotent default-scope compatibility backfill for existing pre-tenant databases; no general migration policy or external storage service  
**Testing**: `dotnet test Aster.sln`, focused tenant definition/resource/query/portability/lifecycle tests, SQLite JSON tenant isolation tests, existing single-tenant regression tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Default single-tenant operations remain equivalent except for cheap tenant-scope resolution; tenant filtering is applied before query predicates and should not require scanning other tenants in provider-backed execution  
**Constraints**: Explicit operation input determines tenant scope; request DTOs carry tenant scope where they already exist; method-style APIs receive additive tenant overloads; omitted scope always maps to the default single-tenant scope; tenant IDs are opaque exact-match values; identities are tenant-scoped; snapshots contain one source tenant and import into one explicit target tenant; no tenant hierarchy, shared definitions, authorization, policy engine, migration framework, runtime scanning, provider registry, public SQL, or public `IQueryable<Resource>`  
**Scale/Scope**: Current core SDK plus in-memory and SQLite JSON provider support for tenant-isolated definitions, resources, activation state, queries, schema upgrades, portability, and lifecycle hook contexts

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library scope contracts only; no UI, CMS, host framework, or authorization coupling.
- **Immutable versioning**: PASS - Tenant scope partitions append-only versions but does not mutate historical snapshots.
- **Channel activation**: PASS - Activation remains separate from resource payloads and is partitioned by tenant plus channel.
- **Typed/queryable**: PASS - Typed aspects and portable query AST remain the public query surface; no public SQL or `IQueryable<Resource>`.
- **Provider agnostic**: PASS - Core models and service contracts carry tenant scope while provider packages handle provider-specific storage filtering.
- **Simplicity first**: PASS - One explicit tenant scope and one default scope satisfy current requirements without tenant hierarchy or policy infrastructure.
- **Modern C# idioms**: PASS - Records, nullable annotations, collection expressions, and async APIs fit the existing SDK style.
- **Readability over cleverness**: PASS - Tenant scope is visible on request models and contexts rather than hidden behind ambient state.
- **Explicitness over magic**: PASS - No runtime scanning, implicit tenant discovery, or hidden host context.
- **Abstractions justified**: PASS - A tenant scope value and scoped request semantics solve demonstrated isolation requirements across existing operations.
- **Optimize for deletion**: PASS - Tenant metadata is additive and localized to request/snapshot/context boundaries and provider filters.
- **Composition over inheritance**: PASS - Uses composed scope values and request data; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No external services, background jobs, migration runner, or deployment-time tenant provisioning.

## Project Structure

### Documentation (this feature)

```text
specs/015-tenant-scoping/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- tenant-scoping.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- tenant-aware request/store contracts
|   +-- Models/Tenancy/
|   |   +-- tenant scope value model and default scope constants
|   +-- Models/Querying/
|   |   +-- query tenant scope metadata
|   +-- Models/Portability/
|   |   +-- snapshot source tenant and import target tenant metadata
|   +-- Models/Lifecycle/
|   |   +-- tenant scope on lifecycle hook contexts
|   +-- InMemory/
|   |   +-- tenant-partitioned definition/resource/portability/query behavior
|   +-- Services/
|       +-- tenant-aware resource manager, schema upgrade, portability, and hook context creation
|
+-- persistence/Aster.Persistence.SqliteJson/
|   +-- tenant-aware SQLite storage and query filtering
|
+-- apps/Aster.Web/
    +-- default-scope compatibility updates only if needed

test/
+-- Aster.Tests/
    +-- Tenancy/
    +-- InMemory/
    +-- Querying/
    +-- Portability/
    +-- Lifecycle/
    +-- SqliteJson/
```

**Structure Decision**: Keep tenant scope in `Aster.Core` because it is a provider-agnostic SDK boundary required by definitions, lifecycle operations, querying, portability, and hooks. Provider packages implement tenant-aware persistence/filtering behind existing provider abstractions. Do not add a tenant service, registry, authorization layer, policy package, or new dependency.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use explicit request-level tenant scope over ambient context.
- Preserve compatibility through a named default single-tenant scope.
- Use request DTO tenant fields where request DTOs already exist and additive overloads for method-style APIs.
- Treat tenant identifiers as opaque exact-match values.
- Make definition and resource identities unique within tenant scope.
- Keep snapshots single-source-tenant and imports single-target-tenant.
- Validate tenant scope centrally and fail closed before reading or writing scoped data.
- Keep provider work explicit and local; SQLite may perform minimal default-scope compatibility bootstrap, but no migration framework, registry, planner, or policy framework is introduced.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/tenant-scoping.md](contracts/tenant-scoping.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design remains SDK-only and leaves tenant auth/policy decisions to hosts.
- **Immutable versioning**: PASS - Tenant-scoped resources still append new immutable versions.
- **Channel activation**: PASS - Activation entries are tenant-scoped while activation stays separate from payload data.
- **Typed/queryable**: PASS - Query model gets tenant scope metadata without exposing SQL or `IQueryable`.
- **Provider agnostic**: PASS - Core defines tenant semantics; providers translate them into local storage filters.
- **Simplicity first**: PASS - Scope value plus request/context propagation is the smallest design that covers current workflows.
- **Modern C# idioms**: PASS - Uses records and nullable-safe request properties consistent with existing code.
- **Readability over cleverness**: PASS - Tenant resolution is explicit and testable at operation boundaries.
- **Explicitness over magic**: PASS - No ambient tenant context, runtime scanning, or convention-only discovery.
- **Abstractions justified**: PASS - Tenant scope model is shared by multiple existing services and providers.
- **Optimize for deletion**: PASS - Tenant-specific additions can be removed from request/snapshot/context fields and provider filters without unrelated framework teardown.
- **Composition over inheritance**: PASS - Scope is composed into existing models; no base-service hierarchy.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflow remains sufficient; no migration engine or tenant provisioning service.
