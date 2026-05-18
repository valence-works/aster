# Implementation Plan: SQLite Date-Like Facet Ranges

**Branch**: `010-sqlite-date-ranges` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

## Summary

Close the SQLite JSON provider's date-like facet range gap by adding explicit translation for accepted ISO-8601-style string facet values. Keep the change provider-local: update SQLite capabilities, validation alignment, translator logic, conformance cases, tests, and docs without changing storage or adding query planning.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies  
**Storage**: Existing resource JSON payloads; no migration or schema changes  
**Testing**: `dotnet test Aster.sln`, `dotnet build Aster.sln`, `git diff --check`  
**Target Platform**: .NET library usable from Generic Host / ASP.NET Core hosts  
**Project Type**: SDK/library with provider package  
**Performance Goals**: Preserve existing query execution behavior; no indexing or planner introduced  
**Constraints**: No public SQL API, public `IQueryable<Resource>`, runtime scanning, provider registry, schema migration, or query planner  
**Scale/Scope**: One provider-local capability gap plus shared conformance coverage

## Constitution Check

- **SDK-first/headless**: PASS — SDK/provider behavior only.
- **Immutable versioning**: PASS — Query-only change.
- **Channel activation**: PASS — Existing scope behavior remains unchanged.
- **Typed/queryable**: PASS — Portable AST semantics are preserved; no `IQueryable`.
- **Provider agnostic**: PASS — Core stays provider-neutral; SQLite implementation is provider-local.
- **Simplicity first**: PASS — Direct translator/capability/test updates.
- **Modern C# idioms**: PASS — Uses existing records, pattern matching, and value-shape model.
- **Readability over cleverness**: PASS — No planner or rewrite framework.
- **Explicitness over magic**: PASS — Accepted date shape is documented and capability-declared.
- **Abstractions justified**: PASS — No new abstraction layer.
- **Optimize for deletion**: PASS — Changes are localized to SQLite range handling and docs/tests.
- **Composition over inheritance**: PASS — No inheritance hierarchy.
- **Intentional dependencies**: PASS — No new dependencies.
- **Operational simplicity**: PASS — No migration, setup, or deployment change.

## Project Structure

```text
src/persistence/Aster.Persistence.SqliteJson/
├── Querying/SqliteWhereTranslator.cs
├── README.md
└── SqliteJsonQueryCapabilitiesProvider.cs

src/core/Aster.Core/
└── README.md

test/Aster.Tests/
├── Querying/ProviderConformanceTests.cs
├── Querying/QueryCapabilityDiscoveryTests.cs
├── Querying/ResourceQueryValidatorTests.cs
└── SqliteJson/SqliteJsonQueryServiceTests.cs

wiki/
├── Querying.md
└── Roadmap.md
```

## Validation

- Focused SQLite date-like range tests
- Provider conformance tests
- `dotnet test Aster.sln`
- `dotnet build Aster.sln`
- `git diff --check`
