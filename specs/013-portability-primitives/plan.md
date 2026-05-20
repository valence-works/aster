# Implementation Plan: Portability Primitives

**Branch**: `013-portability-primitives` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/013-portability-primitives/spec.md`

## Summary

Start Phase 4 with explicit SDK portability primitives for exporting and importing definitions, resources, resource versions, and activation state. The design adds a public `IResourcePortabilityService` over a narrow provider-facing portability store contract so core can validate snapshots, plan deterministic identity mapping, and expose preview/write import results without leaking provider storage details. The first slice stays small: no recipes, lifecycle hooks, live sync, runtime scanning, provider registry, migration engine, public SQL, or public `IQueryable<Resource>`.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK, resource definition/resource version abstractions, in-memory store, SQLite JSON provider, xUnit test stack; no new dependencies
**Storage**: Existing resource definitions, resource versions, and activation state; no schema migration planned for SQLite JSON or in-memory stores
**Testing**: `dotnet test Aster.sln`, focused portability service tests, in-memory and SQLite JSON round-trip tests, invalid snapshot/collision tests, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: Export and preview are linear in selected definitions, selected resource versions, and activation entries; import performs validation and identity-map planning before writes
**Constraints**: Strict import by default; explicit deterministic remap mode; all-or-nothing import; exact definition/resource version identity preservation when no collision exists; no recipes, lifecycle hooks, live sync, migrations, runtime scanning, provider registries, public SQL, or public `IQueryable<Resource>`
**Scale/Scope**: Core portability contracts and orchestration with in-memory and SQLite JSON provider support for deterministic SDK-to-SDK snapshot export/import

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library contracts only; no UI, CMS, or host coupling.
- **Immutable versioning**: PASS - Export/import preserve resource version snapshots and do not rewrite historical versions.
- **Channel activation**: PASS - Activation state remains separate and is represented as activation entries in snapshots.
- **Typed/queryable**: PASS - Typed aspects and the portable query AST are unchanged; no public `IQueryable` or SQL surface.
- **Provider agnostic**: PASS - Core service uses provider-facing portability contracts rather than direct SQLite or in-memory storage access.
- **Simplicity first**: PASS - A small service plus a narrow store capability satisfies the current export/import need without a package repository or sync framework.
- **Modern C# idioms**: PASS - Records, enums, collection expressions, nullable annotations, and async APIs fit the existing SDK style.
- **Readability over cleverness**: PASS - Snapshot validation, scope resolution, and identity mapping are explicit workflows.
- **Explicitness over magic**: PASS - Callers choose export scope, version scope, preview/import, and remap behavior explicitly.
- **Abstractions justified**: PASS - A provider-facing portability store is required because current lifecycle APIs auto-version definitions and do not enumerate activation channels.
- **Optimize for deletion**: PASS - Portability models and service are additive and isolated from querying and schema-version services.
- **Composition over inheritance**: PASS - Uses services and records; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No background processes, deployment changes, schema migrations, or external services.

## Project Structure

### Documentation (this feature)

```text
specs/013-portability-primitives/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- tasks.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
|   +-- portability-primitives.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- portability service contract
|   |   +-- provider-facing portability store contract
|   +-- Models/Portability/
|   |   +-- portable snapshot models
|   |   +-- export/import request and result models
|   |   +-- diagnostics and identity mapping models
|   +-- Services/
|   |   +-- portability service implementation
|   +-- InMemory/
|   |   +-- portability store support
|   +-- Extensions/
|       +-- DI registration update
+-- persistence/Aster.Persistence.SqliteJson/
    +-- SQLite JSON portability store support

test/
+-- Aster.Tests/
    +-- Portability/
    +-- InMemory/
    +-- SqliteJson/
```

**Structure Decision**: Keep public portability contracts in `Aster.Core` because export/import is provider-agnostic SDK behavior. Add provider support in the existing in-memory and SQLite JSON stores because all-version definition enumeration, all-channel activation state, and atomic snapshot writes require provider-owned storage access.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Add `IResourcePortabilityService` as the public workflow surface for export, validation, preview import, and write import.
- Add a narrow provider-facing `IResourcePortabilityStore` for exact snapshot reads and all-or-nothing writes.
- Use SDK-native snapshot records instead of external package formats or new dependencies.
- Treat identical existing content as already satisfied; treat divergent content as a collision.
- Keep import strict by default and require explicit remap mode.
- Scope export through explicit definition/resource and resource-version modes.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/portability-primitives.md](contracts/portability-primitives.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Contracts remain host-agnostic and UI-free.
- **Immutable versioning**: PASS - Snapshot import writes immutable snapshots and does not mutate previous versions.
- **Channel activation**: PASS - Activation state remains an explicit separate snapshot collection.
- **Typed/queryable**: PASS - No query API or typed aspect behavior changes.
- **Provider agnostic**: PASS - Provider-specific work is behind an explicit portability store contract.
- **Simplicity first**: PASS - No repository, recipe, hook, sync, or migration framework.
- **Modern C# idioms**: PASS - Data records and async service methods match the existing codebase.
- **Readability over cleverness**: PASS - Validation and identity mapping are modeled as direct data flows.
- **Explicitness over magic**: PASS - No hidden discovery or automatic remapping.
- **Abstractions justified**: PASS - The new store surface fills concrete read/write gaps in existing lifecycle APIs.
- **Optimize for deletion**: PASS - Portability can be removed without changing query or schema-version surfaces.
- **Composition over inheritance**: PASS - No inheritance introduced.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflow remains sufficient.
