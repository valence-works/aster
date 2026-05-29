# Implementation Plan: Lifecycle Restore Workflows

**Branch**: `018-lifecycle-restore-workflows` | **Date**: 2026-05-27 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/018-lifecycle-restore-workflows/spec.md`

## Summary

Add host-controlled lifecycle restore workflows for archive and soft-delete markers. The implementation adds a small restore service with explicit restore preview and restore application operations, adds request/result models with stable per-candidate diagnostics, and adds a narrow provider-facing marker clear capability so restore can remove existing marker state without rewriting resource versions, changing activation state, adding schedulers, or introducing a general lifecycle state machine.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, lifecycle marker service/store, resource version reader, in-memory store, SQLite JSON provider through existing abstractions, query capability stack, xUnit test stack; no new dependencies  
**Storage**: Existing lifecycle marker storage only. Restore clears existing archive/soft-delete marker rows or in-memory entries; no resource version, activation state, policy declaration, portability snapshot format, or SQLite schema changes.  
**Testing**: `dotnet test Aster.sln`, focused lifecycle restore tests, tenant-scoped restore tests, lifecycle-state query regression tests, SQLite JSON restore compatibility tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Restore preview and application work are bounded by submitted candidate resource IDs in the effective tenant; marker and latest-resource reads are batched by candidate IDs; duplicate handling avoids repeated marker writes.  
**Constraints**: Hosts explicitly submit restore candidates; archive and soft-delete are the only reversible marker states; preview is non-mutating; application clears only the expected marker state; marker-state mismatches fail closed; no resource version rewrites, activation changes, policy declaration mutation, destructive pruning writes, automatic restore, lifecycle hook behavior changes, provider registries, runtime scanning, public SQL, public `IQueryable<Resource>`, schedulers, or hidden jobs.  
**Scale/Scope**: Core SDK models/service/store contract updates plus in-memory and SQLite JSON provider support, docs, and focused tests; no provider-specific restore executor or new storage package.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library contracts only; no UI, CMS, scheduler, authorization, or host framework coupling.
- **Immutable versioning**: PASS - Restore clears lifecycle marker state only and never rewrites resource versions.
- **Channel activation**: PASS - Activation state remains unchanged and separate from marker restore behavior.
- **Typed/queryable**: PASS - Existing lifecycle-state query semantics are preserved; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Core restore logic composes existing resource reader and marker store abstractions; provider details stay in provider packages.
- **Simplicity first**: PASS - A small restore service and marker clear capability are simpler than a workflow engine, registry, or generalized state machine.
- **Modern C# idioms**: PASS - Records, collection expressions, nullable-safe request models, and async APIs match current SDK style.
- **Readability over cleverness**: PASS - Restore preview, validation, and application are direct per-candidate service steps.
- **Explicitness over magic**: PASS - Hosts submit candidates and expected marker states explicitly; no hidden discovery or background behavior.
- **Abstractions justified**: PASS - The restore service groups preview/application behavior without broadening the marker apply contract; the clear capability is required because current storage can save/read markers but cannot remove them through a provider-facing contract.
- **Optimize for deletion**: PASS - Restore models and service methods are additive and localized around lifecycle markers.
- **Composition over inheritance**: PASS - Uses data records and existing composed services; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient; no worker, migration framework, or external service is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/018-lifecycle-restore-workflows/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- lifecycle-restore-workflows.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- lifecycle restore service and marker clear capability contracts
|   +-- Models/Instances/
|   |   +-- lifecycle restore request/result models
|   +-- Models/Policies/
|   |   +-- restore diagnostic codes
|   +-- Services/
|       +-- default lifecycle restore service
|
+-- persistence/Aster.Persistence.SqliteJson/
|   +-- lifecycle marker clear implementation over existing table
|
+-- apps/Aster.Web/
    +-- no feature-specific host UI

test/
+-- Aster.Tests/
    +-- Lifecycle/
    +-- Tenancy/
    +-- Querying/
    +-- SqliteJson/
```

**Structure Decision**: Keep restore workflows in `Aster.Core` because restore is lifecycle marker SDK behavior over existing provider abstractions. Providers only implement marker removal behind a narrow clear capability. Do not add a restore provider registry, scheduler, lifecycle hook pipeline, authorization layer, state-machine package, or new dependency.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Add a small lifecycle restore service instead of expanding direct marker writes or adding a workflow engine.
- Add a narrow provider-facing marker clear capability for stores that support restore.
- Use explicit restore candidates with resource ID and expected archive/soft-delete state.
- Provide non-mutating preview and write-side restore application with parallel result shapes.
- Treat absent markers as already restored, but marker-state mismatches as fail-closed.
- Keep restore tenant-scoped, candidate-bounded, and free of lifecycle hook behavior changes.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/lifecycle-restore-workflows.md](contracts/lifecycle-restore-workflows.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK contracts only and leaves operator decisions and authorization to hosts.
- **Immutable versioning**: PASS - Restore deletes marker state only; resource versions are untouched.
- **Channel activation**: PASS - Activation is neither read as a side effect nor mutated by restore.
- **Typed/queryable**: PASS - Restored resources naturally leave lifecycle-state query results because marker state is cleared; no query API expansion is needed.
- **Provider agnostic**: PASS - Core service depends on existing resource reader and marker store contracts; SQLite details remain provider-local.
- **Simplicity first**: PASS - A focused restore service satisfies current needs without a broader workflow abstraction.
- **Modern C# idioms**: PASS - Planned records/enums/async APIs fit existing code.
- **Readability over cleverness**: PASS - Result models make every restore outcome inspectable.
- **Explicitness over magic**: PASS - Restore only happens through host-submitted candidates.
- **Abstractions justified**: PASS - The restore service avoids widening direct marker writes and the clear capability maps to a concrete storage operation needed by restore.
- **Optimize for deletion**: PASS - Removing restore would remove additive models, service registration, and one narrow clear capability.
- **Composition over inheritance**: PASS - The design composes existing services and records.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No background worker, migration framework, external service, or deployment-time runner is introduced.
