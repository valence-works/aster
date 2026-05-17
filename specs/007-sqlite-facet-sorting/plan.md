# Implementation Plan: SQLite JSON Facet Sorting

**Branch**: `007-sqlite-facet-sorting` | **Date**: 2026-05-17 | **Spec**: [spec.md](spec.md)

## Summary

Enable SQLite JSON facet sorting by reusing the provider's JSON facet lookup logic in both filters and sort orderings. Update SQLite capabilities, validation expectations, conformance coverage, docs, and tests.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing SQLite JSON provider and test stack  
**Storage**: Existing SQLite JSON payload shape; no migration  
**Testing**: `dotnet test Aster.sln`, `dotnet build Aster.sln`, `git diff --check`  

## Constitution Check

- **Simplicity first**: PASS. Reuses existing JSON expression approach.
- **Modern C# idioms**: PASS. Uses small records and collection expressions.
- **Readability over cleverness**: PASS. Keeps lookup construction isolated.
- **Explicitness over magic**: PASS. Capability declaration is updated directly.
- **Abstractions must earn their existence**: PASS. Shared facet expression removes duplicated SQL fragment construction.
- **Dependencies intentional**: PASS. No new dependencies.
- **Operational simplicity**: PASS. No storage or deployment change.

## Validation

- Focused SQLite JSON query and capability tests
- Provider conformance tests
- `dotnet test Aster.sln`
- `dotnet build Aster.sln`
- `git diff --check`
