# Implementation Plan: Portable Operator Expansion

**Branch**: `009-portable-operators` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

## Summary

Expand the existing portable query operator set with `NotEquals`, `In`, `StartsWith`, and facet `Exists`. Keep the implementation direct: extend the operator enum, capability declarations, validator checks, in-memory evaluator, SQLite JSON translator, typed facet helpers, docs, and conformance cases.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, SQLite JSON provider, xUnit test stack; no new dependencies  
**Storage**: Existing resource payloads; no migration  
**Testing**: `dotnet test Aster.sln`, `dotnet build Aster.sln`, `git diff --check`  
**Target Platform**: .NET library usable from Generic Host / ASP.NET Core hosts  
**Project Type**: SDK/library with provider package  
**Constraints**: No public `IQueryable<Resource>`, provider-specific SQL API, runtime scanning, automatic discovery, or query planner

## Constitution Check

- **SDK-first/headless**: PASS — Adds SDK query operators only.
- **Immutable versioning**: PASS — Query-only change.
- **Channel activation**: PASS — Existing query scopes remain unchanged.
- **Typed/queryable**: PASS — Extends the portable AST and typed helpers; no `IQueryable`.
- **Provider agnostic**: PASS — Core operator model remains provider-neutral; providers declare support explicitly.
- **Simplicity first**: PASS — Direct enum/capability/translator/evaluator changes.
- **Modern C# idioms**: PASS — Existing records, collection expressions, and pattern matching are sufficient.
- **Readability over cleverness**: PASS — No expression translator or planner.
- **Explicitness over magic**: PASS — Capabilities and validation expose support directly.
- **Abstractions justified**: PASS — No new abstraction layer.
- **Optimize for deletion**: PASS — Operators are additive and localized.
- **Composition over inheritance**: PASS — No inheritance hierarchy.
- **Intentional dependencies**: PASS — No new dependencies.
- **Operational simplicity**: PASS — No infrastructure or migration changes.

## Project Structure

```text
src/core/Aster.Core/
├── Extensions/TypedQuery.cs
├── InMemory/InMemoryQueryCapabilitiesProvider.cs
├── InMemory/InMemoryQueryService.cs
├── Models/Querying/ComparisonOperator.cs
├── README.md
└── Services/ResourceQueryValidator.cs

src/persistence/Aster.Persistence.SqliteJson/
├── Querying/SqliteTextBehavior.cs
├── Querying/SqliteWhereTranslator.cs
├── README.md
└── SqliteJsonQueryCapabilitiesProvider.cs

test/Aster.Tests/
├── InMemory/InMemoryQueryCapabilityTests.cs
├── Querying/ProviderConformanceTests.cs
├── Querying/ResourceQueryValidatorTests.cs
├── Querying/TypedQueryHelperTests.cs
└── SqliteJson/SqliteJsonQueryCapabilityTests.cs
```

## Validation

- Focused query/operator tests
- Provider conformance tests
- `dotnet test Aster.sln`
- `dotnet build Aster.sln`
- `git diff --check`
