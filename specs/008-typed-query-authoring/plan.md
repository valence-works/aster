# Implementation Plan: Typed Query Authoring Ergonomics

**Branch**: `008-typed-query-authoring` | **Date**: 2026-05-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/008-typed-query-authoring/spec.md`

## Summary

Add small typed authoring helpers over the existing portable query AST. The feature extends `TypedQuery` so typed facet selections can create `SortExpression` values, and adds minimal logical composition helpers for common `And`, `Or`, and `Not` cases. The helpers remain inspectable, provider-agnostic, and validation/execution-compatible because they emit the existing `ResourceQuery`, `FilterExpression`, and `SortExpression` records.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK and xUnit test stack; no new dependencies  
**Storage**: N/A; no persistence or schema changes  
**Testing**: xUnit via `dotnet test Aster.sln`, `dotnet build Aster.sln`, `git diff --check`  
**Target Platform**: .NET library usable from Generic Host / ASP.NET Core hosts  
**Project Type**: SDK/library  
**Performance Goals**: Helper construction should allocate only the existing AST records and small collection wrappers needed for logical operands  
**Constraints**: No public `IQueryable<Resource>`, no LINQ provider, no runtime scanning, no provider registry, no raw SQL, no query planner  
**Scale/Scope**: Typed facet sorting, simple logical composition, documentation, and tests across existing in-memory/SQLite validation and execution paths

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS — Adds SDK helper methods only; no UI/CMS coupling.
- **Immutable versioning**: PASS — Query authoring helpers do not change resource mutation/versioning behavior.
- **Channel activation**: PASS — Activation semantics remain represented by existing `ResourceQuery` scope fields.
- **Typed/queryable**: PASS — Helpers preserve typed aspect member selection and emit the existing portable AST; no public `IQueryable` leakage.
- **Provider agnostic**: PASS — Core helpers are provider-neutral and rely on provider capabilities/validation after AST creation.
- **Simplicity first**: PASS — Extend the existing `TypedQuery` helper surface rather than adding a new builder framework.
- **Modern C# idioms**: PASS — Use records, expression member selection, collection expressions, and nullable annotations already present in the project.
- **Readability over cleverness**: PASS — Expression handling stays limited to direct member selection.
- **Explicitness over magic**: PASS — Helper output is ordinary inspectable query records; overrides are explicit.
- **Abstractions justified**: PASS — New helpers address repeated current call-site friction after facet sorting support landed.
- **Optimize for deletion**: PASS — Helpers are additive and can be removed without changing provider implementations.
- **Composition over inheritance**: PASS — Static helpers and existing records; no inheritance hierarchy.
- **Intentional dependencies**: PASS — No new third-party dependencies.
- **Operational simplicity**: PASS — No storage, deployment, migration, or runtime configuration changes.

## Project Structure

### Documentation (this feature)

```text
specs/008-typed-query-authoring/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── contracts/
│   └── typed-query-authoring.md
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
└── core/
    └── Aster.Core/
        ├── Extensions/
        │   └── TypedQuery.cs
        └── README.md

test/
└── Aster.Tests/
    └── Querying/
        └── TypedQueryHelperTests.cs

wiki/
└── Querying.md
```

**Structure Decision**: Keep the implementation inside `Aster.Core/Extensions/TypedQuery.cs` because the feature is provider-agnostic authoring sugar over public query records. Tests extend the existing typed helper test class. Documentation updates stay in the core README and query wiki.

## Phase 0: Research

Completed in [research.md](research.md).

## Phase 1: Design & Contracts

Completed artifacts:

- [data-model.md](data-model.md)
- [contracts/typed-query-authoring.md](contracts/typed-query-authoring.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS — Public surface remains SDK helper methods.
- **Immutable versioning**: PASS — No persistence mutation changes.
- **Channel activation**: PASS — Existing scope fields remain untouched.
- **Typed/queryable**: PASS — Helpers output the existing AST and avoid `IQueryable<Resource>`.
- **Provider agnostic**: PASS — No provider-specific dependency in core.
- **Simplicity first**: PASS — Minimal additive helper methods.
- **Modern C# idioms**: PASS — Modern C# patterns already used in `TypedQuery` continue.
- **Readability over cleverness**: PASS — No arbitrary expression translation.
- **Explicitness over magic**: PASS — AST output and overrides remain inspectable.
- **Abstractions justified**: PASS — No new abstractions beyond small helpers.
- **Optimize for deletion**: PASS — Additive helpers only.
- **Composition over inheritance**: PASS — No inheritance.
- **Intentional dependencies**: PASS — No new dependencies.
- **Operational simplicity**: PASS — No runtime infrastructure changes.

## Complexity Tracking

No constitution violations identified.
