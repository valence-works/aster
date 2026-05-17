# Implementation Plan: Provider Conformance Tests

**Branch**: `006-provider-conformance-tests` | **Date**: 2026-05-17 | **Spec**: [spec.md](spec.md)

## Summary

Add a small test-only provider conformance harness that exercises active query providers against their declared capabilities. The harness verifies supported query shapes validate and execute, unsupported query shapes fail validation and execution, built-in providers pass the shared checks, and intentionally broken custom fixtures produce focused failures.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting for libraries; tests target net10.0  
**Primary Dependencies**: Existing xUnit and Microsoft.Extensions.DependencyInjection test stack  
**Storage**: Existing in-memory store and disposable SQLite JSON database files  
**Testing**: `dotnet test Aster.sln`, `dotnet build Aster.sln`, `git diff --check`  
**Constraints**: No new runtime dependencies, no provider registry, no runtime scanning, no query planner, no public raw SQL or public `IQueryable<Resource>` API  

## Constitution Check

- **Simplicity first**: PASS. The suite is test-only and explicit.
- **Modern C# idioms**: PASS. Uses records, collection expressions, and nullable-aware service resolution.
- **Readability over cleverness**: PASS. Query cases are named and capability-driven.
- **Explicitness over magic**: PASS. Provider subjects supply services and fixture data explicitly.
- **Abstractions must earn their existence**: PASS. The small harness removes duplicated conformance checks and stays internal to tests.
- **Optimize for deletion**: PASS. The suite is isolated to a single test file plus documentation.
- **Favor composition over inheritance**: PASS. Provider subjects compose setup, expected key, services, and options.
- **Dependencies intentional**: PASS. No new dependencies.
- **Operational simplicity**: PASS. Tests run locally with disposable SQLite files and no external services.

## Project Structure

```text
test/Aster.Tests/Querying/ProviderConformanceTests.cs
wiki/Querying.md
specs/006-provider-conformance-tests/
```

## Implementation Notes

- Keep the conformance harness internal to the test project.
- Treat validation and execution as separate obligations: unsupported shapes must be rejected even if validation is skipped by callers.
- Use explicit provider keys to match active identity and capability declarations.
- Keep provider-specific behavioral tests in place; the conformance suite covers shared expectations only.

## Validation

- `dotnet test test/Aster.Tests/Aster.Tests.csproj --filter FullyQualifiedName~ProviderConformanceTests`
- `dotnet test Aster.sln`
- `dotnet build Aster.sln`
- `git diff --check`
