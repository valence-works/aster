# Implementation Plan: Explicit Indexing Model

**Branch**: `011-explicit-indexing-model` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/011-explicit-indexing-model/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Introduce an explicit provider-declared indexing model in the core SDK. Providers can advertise zero or more index projections through query capabilities, and provider authors can evaluate those projections against resource versions to obtain typed projection values plus structured per-projection failures. Built-in providers declare zero default projections in this slice. The implementation avoids resource-definition changes, runtime scanning, automatic discovery, query planning, public SQL, and public `IQueryable<Resource>`.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies  
**Storage**: Existing resource JSON payloads; no schema migration or physical index creation  
**Testing**: `dotnet test Aster.sln`, `dotnet build Aster.sln`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Projection declaration inspection is in-memory and negligible; projection evaluation is linear in declared projection count per resource version  
**Constraints**: Preserve provider-agnostic core; built-in providers declare zero default projections; no resource-definition index declarations; no planner, scanning, discovery, public SQL, or public `IQueryable<Resource>`  
**Scale/Scope**: Core contract/model slice covering metadata fields and aspect/facet pairs only

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS — Contracts are SDK/library contracts and do not introduce UI or CMS coupling.
- **Immutable versioning**: PASS — Projection evaluation reads immutable resource version snapshots and does not mutate resources.
- **Channel activation**: PASS — Indexing declarations are independent of activation state.
- **Typed/queryable**: PASS — Existing portable query AST semantics remain unchanged; no public `IQueryable` leakage.
- **Provider agnostic**: PASS — Core adds provider-neutral index declarations and evaluation; no database framework dependency.
- **Simplicity first**: PASS — The slice only adds declarations and evaluation, not physical indexing or planning.
- **Modern C# idioms**: PASS — Use small records/enums and nullable-aware result types where they clarify behavior.
- **Readability over cleverness**: PASS — Prefer direct source/value-shape matching over metaprogramming or expression compilation.
- **Explicitness over magic**: PASS — Providers explicitly declare projections; there is no scanning or convention discovery.
- **Abstractions justified**: PASS — New value objects and evaluator serve the current provider authoring and capability-discovery need.
- **Optimize for deletion**: PASS — The model is additive and can be removed from capability descriptions without touching query execution.
- **Composition over inheritance**: PASS — Records and helper services compose behavior without inheritance hierarchies.
- **Intentional dependencies**: PASS — No new third-party dependencies.
- **Operational simplicity**: PASS — No migrations, background services, generated columns, or deployment changes.

## Project Structure

### Documentation (this feature)

```text
specs/011-explicit-indexing-model/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── explicit-indexing-model.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── core/Aster.Core/
│   ├── Models/Querying/
│   │   ├── QueryCapabilityDescription.cs
│   │   ├── QueryDateTimeValue.cs
│   │   └── Indexing model records/enums
│   └── Services/
│       └── Projection evaluation helper/service
└── persistence/Aster.Persistence.SqliteJson/
    └── SqliteJsonQueryCapabilitiesProvider.cs

test/
└── Aster.Tests/
    └── Querying/
        ├── QueryCapabilityDiscoveryTests.cs
        └── Index projection tests

wiki/
└── Querying.md
```

**Structure Decision**: Keep all public indexing contracts in `Aster.Core` near query capability models. Built-in provider capability providers are updated only to expose an empty projection collection. Tests live under `test/Aster.Tests/Querying` because this is a capability/provider-authoring contract, not a SQLite execution feature.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Extend query capabilities instead of adding a separate registry.
- Use provider-declared projections only; resource definitions remain unchanged.
- Restrict sources to metadata fields and aspect/facet pairs.
- Use fail-soft evaluation results with strict value-shape matching.
- Keep built-in providers at zero default projections.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/explicit-indexing-model.md](contracts/explicit-indexing-model.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS — Public surface is SDK-only and host-agnostic.
- **Immutable versioning**: PASS — Evaluator reads snapshots and returns values/failures without mutation.
- **Channel activation**: PASS — No activation behavior changes.
- **Typed/queryable**: PASS — Query AST is preserved; no raw SQL or `IQueryable` introduced.
- **Provider agnostic**: PASS — Index model is database-neutral and provider capability based.
- **Simplicity first**: PASS — No storage, migration, planner, or scanner.
- **Modern C# idioms**: PASS — Records/enums keep the model concise.
- **Readability over cleverness**: PASS — Direct switch/pattern matching over hidden conversion rules.
- **Explicitness over magic**: PASS — Explicit provider declarations only.
- **Abstractions justified**: PASS — Index projections and evaluation results are required by the current spec.
- **Optimize for deletion**: PASS — Additive model can be removed or replaced without changing persistence.
- **Composition over inheritance**: PASS — No inheritance hierarchy introduced.
- **Intentional dependencies**: PASS — No new dependencies.
- **Operational simplicity**: PASS — Existing local build/test workflow remains sufficient.
