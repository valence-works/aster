# Implementation Plan: Definition Schema Versions & Upgrade Flow

**Branch**: `012-definition-schema-upgrades` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/012-definition-schema-upgrades/spec.md`

## Summary

Make resource definition version lineage explicit and actionable for long-lived resources. The current `Resource` model already records `DefinitionVersion` when resources are created, so this slice focuses on preserving that lineage during normal updates, exposing per-resource-version schema status, and adding an explicit append-only upgrade operation that can move the latest resource version to a selected newer definition version. The design preserves aspect data by default, reports carried-forward undeclared data, and avoids migrations, automatic rewriting, provider registries, runtime scanning, query planning, public SQL, or public `IQueryable<Resource>`.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, resource manager/store abstractions, SQLite JSON provider, xUnit test stack; no new dependencies  
**Storage**: Existing resource JSON payloads and definition versions; no schema migration or automatic data rewrite  
**Testing**: `dotnet test Aster.sln`, focused resource schema version tests, SQLite/in-memory lifecycle compatibility tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Schema status checks use bounded definition lookups and a single resource snapshot; upgrades perform the same order of work as a normal resource update plus target-definition validation  
**Constraints**: Append-only resource versions; preserve historical lineage; status is per resource version; upgrades use latest resource version as source; no migrations, background rewriting, runtime scanning, provider registries, query planner, public SQL, or public `IQueryable<Resource>`  
**Scale/Scope**: Core contract and orchestration slice covering definition lineage, status, and explicit upgrades for existing in-memory and SQLite JSON-backed resources

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library contracts only and does not introduce UI or host coupling.
- **Immutable versioning**: PASS - Upgrades append a new resource version and never mutate prior versions.
- **Channel activation**: PASS - Activation state remains independent of resource payloads and schema status.
- **Typed/queryable**: PASS - Typed aspects and query AST behavior are preserved; no `IQueryable` or raw SQL surface is introduced.
- **Provider agnostic**: PASS - Core orchestration uses existing `IResourceDefinitionStore`, `IResourceManager`, and resource version abstractions.
- **Simplicity first**: PASS - Uses a small service and result models rather than a migration engine or transformation framework.
- **Modern C# idioms**: PASS - Records/enums and nullable-aware result models fit the existing SDK style.
- **Readability over cleverness**: PASS - Direct status classification and upgrade branching over hidden conventions.
- **Explicitness over magic**: PASS - Callers explicitly check schema status and explicitly request upgrades.
- **Abstractions justified**: PASS - A dedicated schema-version service is justified by a new public SDK workflow distinct from normal resource updates.
- **Optimize for deletion**: PASS - The service composes existing stores/managers and can be removed without changing provider storage.
- **Composition over inheritance**: PASS - Uses explicit collaborators and result records; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No deployment, provisioning, migration, or background-worker changes.

## Project Structure

### Documentation (this feature)

```text
specs/012-definition-schema-upgrades/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── definition-schema-upgrades.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── core/Aster.Core/
│   ├── Abstractions/
│   │   └── schema version service contract
│   ├── Models/Instances/
│   │   └── schema status and upgrade result models
│   ├── Services/
│   │   └── schema version service implementation
│   └── Extensions/
│       └── DI registration update
└── persistence/Aster.Persistence.SqliteJson/
    └── compatibility only; no storage migration planned

test/
└── Aster.Tests/
    ├── InMemory/
    ├── SqliteJson/
    └── SchemaVersions/
```

**Structure Decision**: Keep the public schema-version contracts in `Aster.Core` because lineage and upgrade orchestration are provider-agnostic SDK behavior. Existing providers already persist the `Resource` snapshot, including `DefinitionVersion`, so provider packages should only need compatibility tests unless implementation discovers a serialization gap.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use existing `Resource.DefinitionVersion` as lineage rather than adding a new resource-version entity.
- Add a small schema-version service instead of expanding `IResourceManager` for every schema-evolution operation.
- Preserve aspect data by default and report carried-forward undeclared data.
- Treat unknown lineage as explicit `unknown-resource-lineage` until upgrade creates a new version.
- Keep upgrades append-only and latest-source-only through existing optimistic concurrency behavior.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/definition-schema-upgrades.md](contracts/definition-schema-upgrades.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Contracts remain host-agnostic and UI-free.
- **Immutable versioning**: PASS - Upgrade results produce append-only resource versions.
- **Channel activation**: PASS - No activation semantics change.
- **Typed/queryable**: PASS - No query model changes and no `IQueryable`.
- **Provider agnostic**: PASS - Implementation composes existing provider abstractions.
- **Simplicity first**: PASS - No migration engine, transformation pipeline, or provider registry.
- **Modern C# idioms**: PASS - Records/enums/result models keep behavior concise.
- **Readability over cleverness**: PASS - Status and upgrade rules are explicit and testable.
- **Explicitness over magic**: PASS - No automatic upgrades or background rewrites.
- **Abstractions justified**: PASS - New service directly represents the user-visible schema-version workflow.
- **Optimize for deletion**: PASS - Additive service and models are isolated from storage internals.
- **Composition over inheritance**: PASS - No inheritance introduced.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflow remains sufficient.
