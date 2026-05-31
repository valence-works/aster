# Implementation Plan: Policy Validation Summaries

**Branch**: `032-policy-validation-summaries` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/032-policy-validation-summaries/spec.md`

## Summary

Add pure host-facing summary records and a `ToSummary()` helper for existing `ResourcePolicyValidationResult` objects. The implementation will live beside existing policy result/summary models, aggregate validation diagnostics deterministically by code, path, policy id, resource id, and resource version, and preserve current policy validation behavior. No provider changes, storage changes, service registration, schedulers, audit persistence, public SQL, public `IQueryable<Resource>`, policy execution changes, or new dependencies are introduced.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK policy models and xUnit test stack; no new dependencies  
**Storage**: N/A. Summaries are pure in-memory views over existing policy validation result objects.  
**Testing**: xUnit through `dotnet test Aster.sln`  
**Target Platform**: .NET library / headless SDK  
**Project Type**: SDK/library  
**Performance Goals**: Summary computation is linear over supplied diagnostics and performs no I/O.  
**Constraints**: No storage/provider/service registration changes; no query planner, public SQL, public `IQueryable<Resource>`, scheduler, audit persistence, policy execution, policy validation behavior, or mutation behavior changes.  
**Scale/Scope**: Single-result summary over one policy validation result; intended for host logging, diagnostics, and dashboards.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host-facing SDK result models only; no UI or CMS coupling.
- **Immutable versioning**: PASS - Does not mutate resource versions or resource payloads.
- **Channel activation**: PASS - Does not touch activation state.
- **Typed/queryable**: PASS - Does not alter typed aspects or query AST semantics; no public `IQueryable`.
- **Provider agnostic**: PASS - Uses existing policy result objects and no provider-specific APIs.
- **Simplicity first**: PASS - Direct summary records and extension helper match existing project patterns.
- **Modern C# idioms**: PASS - Uses records, collection expressions, and simple LINQ grouping where clear.
- **Readability over cleverness**: PASS - Deterministic count helpers stay explicit and easy to inspect.
- **Explicitness over magic**: PASS - Callers explicitly invoke `ToSummary()`.
- **Abstractions justified**: PASS - Summary records match demonstrated host reporting needs and prior summary slices; no new interface/layer.
- **Optimize for deletion**: PASS - The feature is isolated to one model file and focused tests.
- **Composition over inheritance**: PASS - Uses records and functions; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No deployment, migration, provider setup, or observability infrastructure.

## Project Structure

### Documentation (this feature)

```text
specs/032-policy-validation-summaries/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── policy-validation-summaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/core/Aster.Core/
└── Models/
    └── Policies/
        ├── ResourcePolicyResults.cs
        ├── ResourcePolicyApplicationSummaries.cs
        └── ResourcePolicyValidationSummaries.cs

test/Aster.Tests/
└── Policies/
    ├── PolicyValidationTests.cs
    └── PolicyValidationSummaryTests.cs
```

**Structure Decision**: Add a focused policy validation summary model file under existing core policy models and focused tests under existing policy tests. Existing policy validation tests remain unchanged and serve as compatibility coverage.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research will confirm:

- Whether to add a dedicated validation summary file or extend existing policy summary files.
- Whether diagnostic grouping should reuse the existing diagnostic code count record.
- How missing nested diagnostic collections and blank keys should behave.
- Which compatibility tests should prove validation behavior is unchanged.

## Phase 1 Design Summary

Design will produce:

- `research.md` with summary-surface and deterministic grouping decisions.
- `data-model.md` defining summary records and count entities.
- `contracts/policy-validation-summaries.md` documenting the public SDK behavior.
- `quickstart.md` with minimal manually constructed validation summary examples.
- Updated agent context through `.specify/scripts/bash/update-agent-context.sh codex`.

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Public SDK model helper only.
- **Immutable versioning**: PASS - No version changes.
- **Channel activation**: PASS - No activation changes.
- **Typed/queryable**: PASS - No query surface changes.
- **Provider agnostic**: PASS - No provider dependencies.
- **Simplicity first**: PASS - One summary helper and count records, no framework.
- **Modern C# idioms**: PASS - Records and collection expressions are appropriate.
- **Readability over cleverness**: PASS - Counts are explicit and deterministic.
- **Explicitness over magic**: PASS - Explicit helper call only.
- **Abstractions justified**: PASS - No new interfaces or generic pipelines.
- **Optimize for deletion**: PASS - Isolated file and tests.
- **Composition over inheritance**: PASS - Data records and static helpers only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No operational impact.
