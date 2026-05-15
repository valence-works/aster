# Implementation Plan: Query Capabilities & Typed Query Helpers

**Branch**: `003-query-capabilities-typed` | **Date**: 2026-05-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-query-capabilities-typed/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add explicit query capability discovery, structured query preflight validation, and typed query helper APIs that continue to produce the existing portable `ResourceQuery` AST. The design keeps provider differences visible, fails closed when provider capabilities are missing, and avoids introducing a public `IQueryable<Resource>` or provider-specific execution surface.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; SQLite provider keeps existing `Microsoft.Data.Sqlite`  
**Storage**: N/A for core capability models; existing in-memory and SQLite JSON providers declare their current behavior  
**Testing**: xUnit via `dotnet test Aster.sln`  
**Target Platform**: .NET library usable from Generic Host / ASP.NET Core hosts  
**Project Type**: SDK/library with provider package  
**Performance Goals**: Query validation should run in-memory over a `ResourceQuery` AST and complete in negligible time for typical query trees; typed helper construction should allocate only the resulting query/filter records and small mapping metadata  
**Constraints**: No public `IQueryable<Resource>` contract; no silent provider fallback; no new third-party dependencies expected; behavior must remain host-agnostic  
**Scale/Scope**: Current scope covers existing in-memory and SQLite JSON providers, current query AST shapes, common scalar typed aspect members, and per-query typed mapping overrides

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS — Adds library contracts and helpers only; no UI/CMS coupling.
- **Immutable versioning**: PASS — Feature validates/builds queries only and does not alter resource mutation semantics.
- **Channel activation**: PASS — Capabilities and validation must describe existing active/draft scope behavior without moving activation into payloads.
- **Typed/queryable**: PASS — Preserves typed aspects and portable `ResourceQuery`; explicitly forbids public `IQueryable` leakage.
- **Provider agnostic**: PASS — Core capability/validation contracts describe provider behavior without depending on database-specific frameworks.
- **Simplicity first**: PASS — Use direct capability records, a shared validator, and typed helper construction; defer query planner/indexing.
- **Modern C# idioms**: PASS — Records, collection expressions, and expression pattern matching may be used where they clarify immutable query data.
- **Readability over cleverness**: PASS — Avoid expression-tree translation beyond member-name extraction for typed helper mapping.
- **Explicitness over magic**: PASS — Conventions are documented, generated `ResourceQuery` is inspectable, and per-query overrides are explicit.
- **Abstractions justified**: PASS — Capability provider and validator solve current multi-provider discoverability and preflight requirements.
- **Optimize for deletion**: PASS — Capability declarations, validator, and typed helpers can be removed independently of provider execution.
- **Composition over inheritance**: PASS — Prefer records/services/static helpers over inheritance hierarchies.
- **Intentional dependencies**: PASS — No new third-party dependencies expected.
- **Operational simplicity**: PASS — No new infrastructure; debugging improves through structured validation results.

## Project Structure

### Documentation (this feature)

```text
specs/003-query-capabilities-typed/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── query-capabilities.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── core/
│   └── Aster.Core/
│       ├── Abstractions/
│       │   ├── IResourceQueryCapabilitiesProvider.cs
│       │   └── IResourceQueryValidator.cs
│       ├── Extensions/
│       │   └── TypedQuery.cs
│       ├── InMemory/
│       │   └── InMemoryQueryCapabilitiesProvider.cs
│       ├── Models/
│       │   └── Querying/
│       │       ├── QueryCapabilityDescription.cs
│       │       ├── QueryValidationFailure.cs
│       │       ├── QueryValidationResult.cs
│       │       └── TypedQueryOptions.cs
│       └── Services/
│           └── ResourceQueryValidator.cs
├── persistence/
│   └── Aster.Persistence.SqliteJson/
│       └── SqliteJsonQueryCapabilitiesProvider.cs
test/
└── Aster.Tests/
    ├── Querying/
    │   ├── ResourceQueryValidatorTests.cs
    │   └── TypedQueryHelperTests.cs
    ├── InMemory/
    │   └── InMemoryQueryCapabilityTests.cs
    └── SqliteJson/
        └── SqliteJsonQueryCapabilityTests.cs
```

**Structure Decision**: Keep capability models, validator, and typed helper construction in `Aster.Core` because they are provider-agnostic public SDK surface. Provider-specific capability declarations live beside each provider. Tests are split between core query behavior and provider-specific declarations.

## Phase 0: Research

Completed in [research.md](research.md).

## Phase 1: Design & Contracts

Completed artifacts:

- [data-model.md](data-model.md)
- [contracts/query-capabilities.md](contracts/query-capabilities.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS — Public surface remains SDK contracts and helpers.
- **Immutable versioning**: PASS — No persistence mutation changes introduced.
- **Channel activation**: PASS — Active/draft behavior remains represented as query scope capability.
- **Typed/queryable**: PASS — Typed helpers output `ResourceQuery`; no public `IQueryable<Resource>`.
- **Provider agnostic**: PASS — Providers describe capabilities; core validator consumes descriptions.
- **Simplicity first**: PASS — Direct records and one validator are sufficient for the current provider set.
- **Modern C# idioms**: PASS — Plan favors immutable records and concise typed helper APIs.
- **Readability over cleverness**: PASS — Expression use is limited to member selection, not general expression translation.
- **Explicitness over magic**: PASS — Convention, overrides, and produced query records remain inspectable.
- **Abstractions justified**: PASS — New abstractions map directly to spec entities and multi-provider requirements.
- **Optimize for deletion**: PASS — Typed helpers are additive over existing manual `ResourceQuery` construction.
- **Composition over inheritance**: PASS — No inheritance hierarchy planned.
- **Intentional dependencies**: PASS — No new third-party dependencies.
- **Operational simplicity**: PASS — No deployment/runtime infrastructure changes.

## Complexity Tracking

No constitution violations identified.
