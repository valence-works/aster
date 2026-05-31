# Implementation Plan: Portable Validation Summaries

**Branch**: `034-portable-validation-summaries` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/034-portable-validation-summaries/spec.md`

## Summary

Add a pure host-facing summary record and `ToSummary()` helper for existing `PortableSnapshotValidationResult` objects. The implementation will extend the existing portability summary model file, reuse existing diagnostic severity/code count records, aggregate validation diagnostics deterministically, and preserve current portability validation, export, import, provider, storage, and service registration behavior. No new dependencies, storage changes, provider behavior, public SQL, public `IQueryable<Resource>`, reporting framework, or mutation behavior are introduced.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK portability models and xUnit test stack; no new dependencies  
**Storage**: N/A. Summaries are pure in-memory views over supplied portability validation result objects.  
**Testing**: xUnit through `dotnet test Aster.sln`  
**Target Platform**: .NET library / headless SDK  
**Project Type**: SDK/library  
**Performance Goals**: Summary computation is linear over validation diagnostics and performs no I/O.  
**Constraints**: No storage/provider/service registration changes; no reporting framework, query planner, public SQL, public `IQueryable<Resource>`, import/export behavior, validation behavior, or mutation behavior changes.  
**Scale/Scope**: Single validation result summaries intended for host tests, logs, diagnostics, and dashboards.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host-facing SDK model helper only; no UI or CMS coupling.
- **Immutable versioning**: PASS - Does not mutate resource versions or resource payloads.
- **Channel activation**: PASS - Does not touch activation state.
- **Typed/queryable**: PASS - Does not alter typed aspects or query AST semantics; no public `IQueryable`.
- **Provider agnostic**: PASS - Uses existing portability validation result models and no provider-specific APIs.
- **Simplicity first**: PASS - Direct summary record and extension helper match existing project patterns.
- **Modern C# idioms**: PASS - Uses records, collection expressions, and simple LINQ grouping where clear.
- **Readability over cleverness**: PASS - Count helpers stay explicit and deterministic.
- **Explicitness over magic**: PASS - Callers explicitly invoke `ToSummary()`.
- **Abstractions justified**: PASS - Summary record matches demonstrated host reporting needs and existing portability summary patterns; no new interface/layer.
- **Optimize for deletion**: PASS - The feature is isolated to one existing model file and focused tests.
- **Composition over inheritance**: PASS - Uses records and static helpers; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No deployment, migration, provider setup, service registration, or observability infrastructure.

## Project Structure

### Documentation (this feature)

```text
specs/034-portable-validation-summaries/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── portable-validation-summaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/core/Aster.Core/
└── Models/
    └── Portability/
        ├── PortableResults.cs
        ├── PortableDiagnostic.cs
        └── PortableResultSummaries.cs

test/Aster.Tests/
└── Portability/
    ├── PortableResultSummaryTests.cs
    └── PortabilityValidationTests.cs
```

**Structure Decision**: Extend the existing portability result summary file and tests because validation summaries share the same diagnostic count records and ordering rules as export/import summaries. Existing portability validation tests remain unchanged and serve as compatibility coverage.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research will confirm:

- Whether to extend existing portability result summaries or introduce a separate validation summary file.
- Which validation fields and diagnostic dimensions are useful without adding reporting infrastructure.
- How null diagnostics and blank diagnostic codes should behave.
- Which compatibility tests should prove validation behavior is unchanged.

## Phase 1 Design Summary

Design will produce:

- `research.md` with summary-surface and deterministic grouping decisions.
- `data-model.md` defining the validation summary record and reused count entities.
- `contracts/portable-validation-summaries.md` documenting public SDK behavior.
- `quickstart.md` with minimal validation summary examples.
- Updated agent context through `.specify/scripts/bash/update-agent-context.sh codex`.

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Public SDK model helper only.
- **Immutable versioning**: PASS - No version changes.
- **Channel activation**: PASS - No activation changes.
- **Typed/queryable**: PASS - No query surface changes.
- **Provider agnostic**: PASS - No provider dependencies.
- **Simplicity first**: PASS - One summary helper surface, no framework.
- **Modern C# idioms**: PASS - Records and collection expressions are appropriate.
- **Readability over cleverness**: PASS - Counts are explicit and deterministic.
- **Explicitness over magic**: PASS - Explicit helper call only.
- **Abstractions justified**: PASS - Reuses existing summary patterns and count records.
- **Optimize for deletion**: PASS - Isolated file and tests.
- **Composition over inheritance**: PASS - Data records and static helpers only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No operational impact.
