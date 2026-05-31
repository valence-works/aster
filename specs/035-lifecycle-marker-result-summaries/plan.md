# Implementation Plan: Lifecycle Marker Result Summaries

**Branch**: `035-lifecycle-marker-result-summaries` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/035-lifecycle-marker-result-summaries/spec.md`

## Summary

Add pure host-facing summary records and `ToSummary()` helpers for existing `ResourceLifecycleMarkerResult` objects. The implementation will live beside existing lifecycle marker models, aggregate supplied marker results deterministically by success/failure, marker state, marker resource identifier, policy diagnostic code, policy diagnostic path, and diagnostic resource identifier, and preserve current marker service/store behavior. No provider changes, storage changes, service registration, reporting framework, public SQL, public `IQueryable<Resource>`, marker write behavior, policy behavior, or new dependencies are introduced.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK lifecycle marker and policy diagnostic models plus xUnit test stack; no new dependencies  
**Storage**: N/A. Summaries are pure in-memory views over supplied lifecycle marker result objects.  
**Testing**: xUnit through `dotnet test Aster.sln`  
**Target Platform**: .NET library / headless SDK  
**Project Type**: SDK/library  
**Performance Goals**: Summary computation is linear over supplied marker results and nested diagnostics and performs no I/O.  
**Constraints**: No storage/provider/service registration changes; no query planner, public SQL, public `IQueryable<Resource>`, reporting framework, marker service behavior, marker store behavior, policy behavior, or mutation behavior changes.  
**Scale/Scope**: Single-result and enumerable-result summaries intended for host tests, logs, diagnostics, and dashboards.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host-facing SDK model helpers only; no UI or CMS coupling.
- **Immutable versioning**: PASS - Does not mutate resource versions or resource payloads.
- **Channel activation**: PASS - Does not touch activation state.
- **Typed/queryable**: PASS - Does not alter typed aspects or query AST semantics; no public `IQueryable`.
- **Provider agnostic**: PASS - Uses existing marker result and policy diagnostic model objects and no provider-specific APIs.
- **Simplicity first**: PASS - Direct summary records and extension helpers match existing project patterns.
- **Modern C# idioms**: PASS - Uses records, collection expressions, and simple LINQ grouping where clear.
- **Readability over cleverness**: PASS - Count helpers stay explicit and deterministic.
- **Explicitness over magic**: PASS - Callers explicitly invoke `ToSummary()`.
- **Abstractions justified**: PASS - Summary records match demonstrated host reporting needs and prior summary slices; no new interface/layer.
- **Optimize for deletion**: PASS - The feature is isolated to one model file and focused tests.
- **Composition over inheritance**: PASS - Uses records and functions; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No deployment, migration, provider setup, service registration, or observability infrastructure.

## Project Structure

### Documentation (this feature)

```text
specs/035-lifecycle-marker-result-summaries/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── lifecycle-marker-result-summaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/core/Aster.Core/
└── Models/
    └── Instances/
        ├── ResourceLifecycleMarker.cs
        └── ResourceLifecycleMarkerSummaries.cs

test/Aster.Tests/
└── Policies/
    ├── LifecycleMarkerServiceTests.cs
    ├── LifecycleMarkerConflictTests.cs
    └── LifecycleMarkerResultSummaryTests.cs
```

**Structure Decision**: Add a focused lifecycle marker summary model file under existing instance models and focused tests under existing policy/lifecycle marker tests. Existing marker service tests remain unchanged and serve as compatibility coverage.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research will confirm:

- Whether to summarize a single marker result, multiple marker results, or both.
- Which marker and diagnostic dimensions are useful without adding marker service state.
- How null result collections, null entries, null nested diagnostics, and blank keys should behave.
- Which compatibility tests should prove marker service behavior is unchanged.

## Phase 1 Design Summary

Design will produce:

- `research.md` with summary-surface and deterministic grouping decisions.
- `data-model.md` defining summary records and count entities.
- `contracts/lifecycle-marker-result-summaries.md` documenting public SDK behavior.
- `quickstart.md` with minimal manually constructed marker result summary examples.
- Updated agent context through `.specify/scripts/bash/update-agent-context.sh codex`.

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Public SDK model helper only.
- **Immutable versioning**: PASS - No version changes.
- **Channel activation**: PASS - No activation changes.
- **Typed/queryable**: PASS - No query surface changes.
- **Provider agnostic**: PASS - No provider dependencies.
- **Simplicity first**: PASS - One summary helper surface and count records, no framework.
- **Modern C# idioms**: PASS - Records and collection expressions are appropriate.
- **Readability over cleverness**: PASS - Counts are explicit and deterministic.
- **Explicitness over magic**: PASS - Explicit helper call only.
- **Abstractions justified**: PASS - No new interfaces or generic pipelines.
- **Optimize for deletion**: PASS - Isolated file and tests.
- **Composition over inheritance**: PASS - Data records and static helpers only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No operational impact.
