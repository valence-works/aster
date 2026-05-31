# Implementation Plan: Schema Upgrade Summaries

**Branch**: `029-schema-upgrade-summaries` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/029-schema-upgrade-summaries/spec.md`

## Summary

Add pure host-facing summary records and `ToSummary()` helpers for existing schema status inspection and schema upgrade result objects. The implementation will live in core model code beside the existing schema status/upgrade models, use deterministic in-memory grouping/counting only, and add focused xUnit coverage. It will not introduce services, provider changes, storage changes, schedulers, audit persistence, public SQL, public `IQueryable<Resource>`, or new dependencies.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK schema-version models and xUnit test stack; no new dependencies  
**Storage**: No storage changes. Summaries are pure in-memory views over existing schema status and schema upgrade result objects; no schema migration, persistence, provider storage, audit records, or physical index changes.  
**Testing**: `dotnet test Aster.sln`, focused schema summary tests, existing schema version tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET library consumed by host applications  
**Project Type**: SDK/library  
**Performance Goals**: Linear in supplied result count; no provider access or extra resource scans  
**Constraints**: Pure transformations only; deterministic ordering; null-as-empty for collection inputs; no mutation; no new runtime services  
**Scale/Scope**: One bounded reporting slice over schema status and schema upgrade result objects

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host-agnostic SDK records/extensions only.
- **Immutable versioning**: PASS - Does not create, rewrite, or delete resource versions.
- **Channel activation**: PASS - Does not touch activation state.
- **Typed/queryable**: PASS - Does not change typed aspect binding or query AST semantics; no public `IQueryable`.
- **Provider agnostic**: PASS - Core-only pure aggregation over existing result objects.
- **Simplicity first**: PASS - Small records and extension helpers are the simplest current implementation.
- **Modern C# idioms**: PASS - Uses records, collection expressions, LINQ grouping, and nullability-aware argument validation where clear.
- **Readability over cleverness**: PASS - Deterministic counting helpers stay direct and reviewable.
- **Explicitness over magic**: PASS - Hosts explicitly call summary helpers; no scanning or implicit registration.
- **Abstractions justified**: PASS - No new service/interface layer; records/extensions match existing summary slices.
- **Optimize for deletion**: PASS - One isolated model file and focused tests can be removed without affecting execution behavior.
- **Composition over inheritance**: PASS - Uses records and static helpers, no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No deployment, migration, provider setup, scheduler, or observability changes.

## Project Structure

### Documentation (this feature)

```text
specs/029-schema-upgrade-summaries/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── schema-upgrade-summaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/core/Aster.Core/
└── Models/Instances/
    ├── ResourceSchemaStatus.cs
    ├── ResourceSchemaUpgrade.cs
    └── ResourceSchemaUpgradeSummaries.cs

test/Aster.Tests/
└── SchemaVersions/
    ├── ResourceSchemaVersionServiceTests.cs
    └── ResourceSchemaUpgradeSummaryTests.cs
```

**Structure Decision**: Add a new summary model file under `Models/Instances` to keep the existing schema status and schema upgrade model files stable and avoid growing execution-oriented models. Add focused tests under existing schema-version tests.

## Complexity Tracking

No constitution violations.

## Phase 0: Research

See [research.md](research.md).

## Phase 1: Design & Contracts

See [data-model.md](data-model.md), [contracts/schema-upgrade-summaries.md](contracts/schema-upgrade-summaries.md), and [quickstart.md](quickstart.md).

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Public surface is host-facing records/extensions only.
- **Immutable versioning**: PASS - Summaries do not perform schema upgrades or writes.
- **Channel activation**: PASS - Activation state remains untouched.
- **Typed/queryable**: PASS - No query changes or queryable leakage.
- **Provider agnostic**: PASS - No provider-specific implementation.
- **Simplicity first**: PASS - No registry, service, planner, or execution abstraction.
- **Modern C# idioms**: PASS - Records and collection expressions improve clarity.
- **Readability over cleverness**: PASS - Counting rules are explicit and deterministic.
- **Explicitness over magic**: PASS - Explicit `ToSummary()` calls only.
- **Abstractions justified**: PASS - Summary records match demonstrated host reporting needs and prior slices.
- **Optimize for deletion**: PASS - Isolated pure helper file.
- **Composition over inheritance**: PASS - No inheritance.
- **Intentional dependencies**: PASS - No dependencies added.
- **Operational simplicity**: PASS - No operational footprint.
