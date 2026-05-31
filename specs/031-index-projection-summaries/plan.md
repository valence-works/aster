# Implementation Plan: Index Projection Summaries

**Branch**: `031-index-projection-summaries` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/031-index-projection-summaries/spec.md`

## Summary

Add pure host-facing summary records and `ToSummary()` helpers for existing `IndexProjectionValidationResult` and `IndexProjectionEvaluationResult` objects. The implementation will live beside the query projection models, aggregate projection failures deterministically by code, field name, and source, and aggregate successful evaluation values by field type and field name. No physical indexes, provider changes, query planner, service registration, storage changes, public SQL, public `IQueryable<Resource>`, execution behavior changes, or new dependencies are introduced.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK index projection models and xUnit test stack; no new dependencies  
**Storage**: No storage changes. Summaries are pure in-memory views over existing projection validation/evaluation result objects; no schema migration, persistence, provider storage, audit records, physical indexes, or physical index changes.  
**Testing**: `dotnet test Aster.sln`, focused index projection summary tests, existing index projection declaration/evaluation tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET library consumed by host applications and provider authors  
**Project Type**: SDK/library  
**Performance Goals**: Linear in supplied value/failure count; no provider access, physical indexing, query planning, or query execution  
**Constraints**: Pure transformations only; deterministic ordering; ignore blank key-specific buckets; no mutation; no new runtime services  
**Scale/Scope**: One bounded reporting slice over index projection validation/evaluation result objects

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host/provider-author SDK records/extensions only.
- **Immutable versioning**: PASS - Does not create, rewrite, or delete resource versions.
- **Channel activation**: PASS - Does not touch activation state.
- **Typed/queryable**: PASS - Does not change typed aspect binding or query AST semantics; no public `IQueryable`.
- **Provider agnostic**: PASS - Core-only pure aggregation over existing projection results.
- **Simplicity first**: PASS - Small records and extension helpers are the simplest current implementation.
- **Modern C# idioms**: PASS - Uses records, collection expressions, LINQ grouping, and nullability-aware argument validation where clear.
- **Readability over cleverness**: PASS - Counting rules stay direct and reviewable.
- **Explicitness over magic**: PASS - Hosts explicitly call summary helpers; no scanning or implicit registration.
- **Abstractions justified**: PASS - No new service/interface layer; records/extensions match existing summary slices.
- **Optimize for deletion**: PASS - One isolated model file and focused tests can be removed without affecting projection behavior.
- **Composition over inheritance**: PASS - Uses records and static helpers, no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No deployment, migration, provider setup, scheduler, or observability changes.

## Project Structure

### Documentation (this feature)

```text
specs/031-index-projection-summaries/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── index-projection-summaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/core/Aster.Core/
└── Models/Querying/
    ├── IndexProjectionValidation.cs
    ├── IndexProjections.cs
    └── IndexProjectionSummaries.cs

test/Aster.Tests/
└── Querying/
    ├── IndexProjectionSummaryTests.cs
    ├── IndexProjectionDeclarationTests.cs
    └── IndexProjectionEvaluationTests.cs
```

**Structure Decision**: Add a new summary model file under `Models/Querying` to keep existing projection validation/evaluation result contracts stable and avoid mixing aggregate helper code into core projection result records.

## Complexity Tracking

No constitution violations.

## Phase 0: Research

See [research.md](research.md).

## Phase 1: Design & Contracts

See [data-model.md](data-model.md), [contracts/index-projection-summaries.md](contracts/index-projection-summaries.md), and [quickstart.md](quickstart.md).

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Public surface is host/provider-author records/extensions only.
- **Immutable versioning**: PASS - No resource mutation.
- **Channel activation**: PASS - Activation state remains untouched.
- **Typed/queryable**: PASS - No query shape changes or queryable leakage.
- **Provider agnostic**: PASS - No provider-specific implementation.
- **Simplicity first**: PASS - No registry, service, physical index, planner, or execution abstraction.
- **Modern C# idioms**: PASS - Records and collection expressions improve clarity.
- **Readability over cleverness**: PASS - Counting rules are explicit and deterministic.
- **Explicitness over magic**: PASS - Explicit `ToSummary()` calls only.
- **Abstractions justified**: PASS - Summary records match demonstrated host reporting needs and prior slices.
- **Optimize for deletion**: PASS - Isolated pure helper file.
- **Composition over inheritance**: PASS - No inheritance.
- **Intentional dependencies**: PASS - No dependencies added.
- **Operational simplicity**: PASS - No operational footprint.
