# Implementation Plan: Policy Pruning Application

**Branch**: `019-policy-pruning-application` | **Date**: 2026-05-29 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/019-policy-pruning-application/spec.md`

## Summary

Add host-controlled policy pruning application for selected version-pruning preview outcomes. The implementation adds explicit request/result models, a small application service that revalidates policy and current resource state before destructive removal, and a narrow provider-facing resource version pruning capability. Pruning remains tenant-scoped, candidate-bounded, partial-success capable, and separate from archive, soft-delete, restore, activation, portability format changes, schedulers, authorization, provider registries, public SQL, and public queryable resource surfaces.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, resource definition store, resource version reader, lifecycle marker store, policy validation/evaluation models, in-memory store, SQLite JSON provider, xUnit test stack; no new dependencies  
**Storage**: Existing resource version storage only. Pruning removes selected resource version snapshots from in-memory and SQLite JSON stores; no schema migration, policy declaration mutation, lifecycle marker mutation, activation mutation, or portability snapshot format change.  
**Testing**: `dotnet test Aster.sln`, focused policy pruning application tests, tenant-scoped pruning tests, SQLite JSON pruning tests, provider unsupported/fail-closed tests, existing policy/restore/portability regression tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Application work is bounded by submitted candidate resource IDs and versions in one effective tenant; current resource reads are batched by resource ID; duplicate candidates avoid repeated remove attempts.  
**Constraints**: Hosts explicitly submit candidates derived from pruning previews; removal is destructive; current resource existence, policy declaration, policy criteria, activation, lifecycle marker, latest-version, and retained-version safety are rechecked before each removal; no background jobs, automatic retention, authorization engine, broad workflow state machine, runtime scanning, provider registry, public SQL, public `IQueryable<Resource>`, or schema migration.  
**Scale/Scope**: Core SDK models/service/store contract updates plus in-memory and SQLite JSON provider support, docs, and focused tests; no provider-specific pruning planner or migration framework.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK contracts and services only; hosts remain responsible for UI, authorization, and operator workflow.
- **Immutable versioning**: PASS - Existing behavior remains append-only by default; destructive pruning is explicit, host-selected, and limited to a separate pruning workflow.
- **Channel activation**: PASS - Active versions are protected and activation state is not mutated by pruning.
- **Typed/queryable**: PASS - Existing query AST semantics remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Core service composes provider-facing abstractions; provider-specific removal stays in provider packages.
- **Simplicity first**: PASS - A focused pruning service and narrow remove capability are simpler than a scheduler, registry, planner, or generalized lifecycle state machine.
- **Modern C# idioms**: PASS - Records, collection expressions, nullable-safe request handling, async APIs, and pattern matching fit existing code style.
- **Readability over cleverness**: PASS - Candidate validation, preflight, and removal are explicit service steps with inspectable result models.
- **Explicitness over magic**: PASS - Hosts submit all candidates explicitly; no hidden discovery, runtime scanning, or ambient tenant behavior.
- **Abstractions justified**: PASS - A provider-facing pruning capability is required because current write contracts cannot delete version snapshots.
- **Optimize for deletion**: PASS - Models, service, and provider capability are additive and localized around pruning.
- **Composition over inheritance**: PASS - Uses services, records, and explicit collaborators; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient; no worker, migration, or external service is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/019-policy-pruning-application/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- policy-pruning-application.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- policy pruning service and provider pruning capability contracts
|   +-- Models/Policies/
|   |   +-- policy pruning application request/result models and diagnostics
|   +-- Services/
|       +-- default policy pruning application service
|
+-- persistence/Aster.Persistence.SqliteJson/
|   +-- resource version pruning implementation over existing resource storage
|
+-- apps/Aster.Web/
    +-- no feature-specific host UI

test/
+-- Aster.Tests/
    +-- Policies/
    +-- Tenancy/
    +-- SqliteJson/
    +-- Portability/
```

**Structure Decision**: Keep pruning application in `Aster.Core` because selection validation, tenant boundaries, policy compatibility, and safety preflight are SDK semantics. Providers only implement conditional resource version removal behind a narrow capability. Do not add a provider registry, scheduler, authorization layer, policy engine package, migration framework, or new dependency.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Add a dedicated host-controlled pruning application service rather than expanding lifecycle marker application.
- Add a narrow provider-facing resource version pruning capability because existing writer abstractions cannot remove version snapshots.
- Use preview-derived candidate fields without an opaque preview token.
- Revalidate current policy declaration, policy criteria, activation state, lifecycle marker state, latest status, version existence, and retained-version safety before removal.
- Treat duplicate and already-pruned candidates deterministically.
- Preserve tenant scoping and partial-success result semantics.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/policy-pruning-application.md](contracts/policy-pruning-application.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK contracts only and leaves operator decisions and authorization to hosts.
- **Immutable versioning**: PASS - Destructive removal is explicit and bounded; normal save/update behavior remains append-only.
- **Channel activation**: PASS - Active versions are detected and protected; activation rows are not updated as a side effect.
- **Typed/queryable**: PASS - Pruned versions naturally stop appearing in existing reads/queries; query surface is unchanged.
- **Provider agnostic**: PASS - Core service uses existing stores plus one narrow provider capability.
- **Simplicity first**: PASS - No transaction coordinator, scheduler, registry, or planner is introduced.
- **Modern C# idioms**: PASS - Planned models and services follow existing modern C# style.
- **Readability over cleverness**: PASS - Result models make every destructive outcome explicit.
- **Explicitness over magic**: PASS - Hosts submit candidates and tenant scope explicitly.
- **Abstractions justified**: PASS - Version removal requires a provider boundary not present in current writer contracts.
- **Optimize for deletion**: PASS - Removing the feature would remove additive contracts/models/service and provider methods.
- **Composition over inheritance**: PASS - The design composes existing services and provider capabilities.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No background worker, migration framework, external service, or deployment-time runner is introduced.
