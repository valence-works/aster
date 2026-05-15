# Implementation Plan: SQLite JSON Querying (Phase 2A)

**Branch**: `002-sqlite-json-querying` | **Date**: 2026-05-15 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/002-sqlite-json-querying/spec.md`

## Summary

Implement a SQLite-specific `IResourceQueryService` for `Aster.Persistence.SqliteJson` that executes supported `ResourceQuery` shapes directly in SQLite. The first slice supports metadata filters, version scopes, metadata sorting, and paging. The second slice adds simple scalar JSON facet filtering using SQLite JSON functions. Unsupported query shapes fail explicitly with `UnsupportedQueryFeatureException`; there is no hidden in-memory fallback.

## Technical Context

**Language/Version**: C# / .NET 8.0, 9.0, 10.0 multi-targeted libraries  
**Primary Dependencies**: `Aster.Core`, `Microsoft.Data.Sqlite` 9.0.0  
**Storage**: SQLite file database with table-plus-JSON-payload schema from `Aster.Persistence.SqliteJson`  
**Testing**: xUnit via `dotnet test Aster.sln`  
**Target Platform**: Cross-platform .NET library  
**Project Type**: Library/provider package plus integration tests  
**Performance Goals**: Avoid full-table materialization for supported metadata/scope/paging queries; keep query execution in SQLite for supported predicates  
**Constraints**: No `IQueryable` provider, no new runtime dependency, no generic cross-provider query planner, no silent in-memory fallback  
**Scale/Scope**: Phase 2A provider query path for SQLite JSON; not a full indexing/capability framework

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS — adds provider implementation behind existing SDK contracts; no host/UI coupling.
- **Immutable versioning**: PASS — queries read existing immutable resource version snapshots.
- **Channel activation**: PASS — active/draft scopes continue to use activation state separate from payloads.
- **Typed/queryable**: PASS — keeps `ResourceQuery` AST as the contract and explicitly rejects public `IQueryable`.
- **Provider agnostic**: PASS — SQLite-specific code remains in `Aster.Persistence.SqliteJson`; core contracts remain provider-neutral.
- **Simplicity first**: PASS — direct provider translator for current query shapes; no generic planner.
- **Modern C# idioms**: PASS — use records/existing models, pattern matching, collection expressions where they clarify.
- **Readability over cleverness**: PASS — prefer small translator helpers over dynamic metaprogramming.
- **Explicitness over magic**: PASS — provider query service is explicitly registered by `AddAsterSqliteJson(...)`.
- **Abstractions justified**: PASS — any helper abstractions are internal and exist only to separate SQL text, parameters, and AST translation.
- **Optimize for deletion**: PASS — SQLite query service can be removed/replaced without changing core AST contracts.
- **Composition over inheritance**: PASS — uses service composition and small helpers; no inheritance hierarchy.
- **Intentional dependencies**: PASS — no new dependency beyond existing `Microsoft.Data.Sqlite`.
- **Operational simplicity**: PASS — file-based SQLite and `dotnet test`; no external services.

## Project Structure

### Documentation (this feature)

```text
specs/002-sqlite-json-querying/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── core/
│   └── Aster.Core/
│       ├── Abstractions/
│       ├── Exceptions/
│       └── Models/Querying/
└── persistence/
    └── Aster.Persistence.SqliteJson/
        ├── SqliteJsonResourceStore.cs
        ├── SqliteJsonQueryService.cs
        ├── SqliteJsonAsterServiceCollectionExtensions.cs
        └── Querying/
            ├── SqliteQueryBuilder.cs
            ├── SqliteWhereTranslator.cs
            ├── SqliteJsonPath.cs
            └── SqliteParameterBag.cs

test/
└── Aster.Tests/
    └── SqliteJson/
        ├── SqliteJsonResourceStoreTests.cs
        └── SqliteJsonQueryServiceTests.cs
```

**Structure Decision**: Keep SQLite-specific query translation inside the provider package. Core remains unchanged except for possible error-message refinements if tests expose unclear unsupported-query diagnostics.

## Complexity Tracking

No constitution violations identified.

## Phase 0: Research

See [research.md](./research.md).

## Phase 1: Design

Design outputs:

- [data-model.md](./data-model.md)
- [quickstart.md](./quickstart.md)
- No new public API contracts are expected beyond provider registration behavior; existing `IResourceQueryService` is the contract.

## Implementation Strategy

1. Add failing SQLite query integration tests for P1 metadata/scope/sort/page behavior.
2. Implement `SqliteJsonQueryService` and register it from `AddAsterSqliteJson(...)`.
3. Add failing P2 tests for aspect presence and scalar facet `Equals`, `Contains`, and `Range`.
4. Implement SQLite JSON path translation using provider-owned helpers.
5. Add P3 tests for unsupported query shapes and invalid query inputs.
6. Verify in-memory query tests still pass unchanged.
7. Update provider README and query docs with supported SQLite subset.

## Risk Notes

- SQLite JSON path construction is the primary injection-sensitive area; paths must be built from validated segments, not interpolated arbitrary user strings.
- `Contains` semantics are simple SQLite substring matching for this feature, not full-text or culture-aware search.
- Date-like facet range behavior is out of scope until typed index metadata exists. Numeric facet ranges and metadata `Created` sorting/filtering are the only range-like behavior in this slice.
