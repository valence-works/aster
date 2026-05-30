# Implementation Plan: Batch Version History Inspection

**Branch**: `023-batch-version-history` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/023-batch-version-history/spec.md`

## Summary

Add a small batch read surface to the existing read-only resource version history service. The implementation adds request/result models and a service method that accepts an explicit bounded resource ID selection, resolves one tenant scope, reuses existing version, activation-state, and lifecycle marker reads, and returns one history per distinct requested resource ID. The feature adds no storage changes, no provider registry, no public SQL, no public `IQueryable<Resource>`, no query planner, and no automatic discovery.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK, resource version history service, resource version reader, activation-state reader, lifecycle marker store, in-memory store, SQLite JSON provider, xUnit test stack; no new dependencies
**Storage**: Existing resource versions, activation state, and lifecycle marker storage only. No schema migration, data rewrite, portability snapshot format change, or physical index creation.
**Testing**: `dotnet test Aster.sln`, focused version history tests, tenant isolation tests, SQLite JSON parity tests, compatibility tests for existing single-resource history behavior, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: Batch inspection is bounded to an explicit caller-supplied identifier set in one tenant and should reuse bulk-capable existing reads rather than forcing callers into repeated service calls.
**Constraints**: Read-only behavior; one effective tenant per request; deterministic first-seen distinct resource order; deterministic version and active-channel ordering; empty selection succeeds; blank identifiers fail fast; missing resources return empty histories; no scheduler, authorization engine, provider registry, runtime scanning, public SQL, public `IQueryable<Resource>`, query planner, broad reporting infrastructure, or schema migration.
**Scale/Scope**: Core SDK request/result models, one method on the existing history service, service implementation reuse, docs, roadmap, and focused tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host-facing SDK contracts only; no UI, CMS, or host authorization behavior.
- **Immutable versioning**: PASS - Batch inspection is read-only and does not mutate resource versions.
- **Channel activation**: PASS - Activation remains separate from resource payloads; batch history only reports active channels.
- **Typed/queryable**: PASS - Existing typed aspects and query AST remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Core service composes existing abstractions and does not introduce provider-specific logic.
- **Simplicity first**: PASS - Extends the existing history service instead of adding a registry, planner, reporting framework, or new provider surface.
- **Modern C# idioms**: PASS - Records, collection expressions, nullable-safe request handling, async APIs, and simple LINQ/grouping fit existing style.
- **Readability over cleverness**: PASS - Batch assembly remains explicit: normalize IDs, read state once, project histories.
- **Explicitness over magic**: PASS - Hosts submit resource IDs and tenant scope explicitly; no scanning or discovery.
- **Abstractions justified**: PASS - No new abstraction is planned; the existing service boundary already owns version history inspection.
- **Optimize for deletion**: PASS - Removing the feature would remove additive models, one method, tests, and docs without storage cleanup.
- **Composition over inheritance**: PASS - Uses services, records, and explicit collaborators; no new inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No migration, worker, external service, or setup change.

## Project Structure

### Documentation (this feature)

```text
specs/023-batch-version-history/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- batch-version-history.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- IResourceVersionHistoryService.cs
|   +-- Models/Instances/
|   |   +-- ResourceVersionHistory.cs
|   +-- Services/
|       +-- ResourceVersionHistoryService.cs
|
+-- persistence/Aster.Persistence.SqliteJson/
    +-- no schema changes or provider-specific batch API

test/
+-- Aster.Tests/
    +-- Versioning/
    +-- Tenancy/
    +-- SqliteJson/
```

**Structure Decision**: Keep batch inspection in `Aster.Core` beside the existing single-resource history service. Provider packages already expose the necessary version, activation, and marker reads through existing abstractions, so no new provider registry, provider batch API, storage migration, or query surface is needed.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Extend the existing history service rather than creating a second service.
- Add batch request/result models that preserve existing single-resource result shape.
- Normalize resource IDs by first-seen ordinal distinct order.
- Return empty histories for missing resources and empty batch results for empty selections.
- Reuse existing provider abstractions and storage.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/batch-version-history.md](contracts/batch-version-history.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK services and models only.
- **Immutable versioning**: PASS - No write path is introduced.
- **Channel activation**: PASS - Active channels are read from activation state and remain decoupled from resource payloads.
- **Typed/queryable**: PASS - Query AST and typed aspect APIs remain unchanged.
- **Provider agnostic**: PASS - Core service depends on existing reader abstractions; no provider-specific branch is introduced.
- **Simplicity first**: PASS - Scope remains one batch read method over existing semantics.
- **Modern C# idioms**: PASS - Planned models and service updates follow existing modern C# style.
- **Readability over cleverness**: PASS - The algorithm is intentionally direct and testable.
- **Explicitness over magic**: PASS - Tenant scope and resource IDs are explicit request fields.
- **Abstractions justified**: PASS - No new abstraction is added.
- **Optimize for deletion**: PASS - Feature is additive and localized.
- **Composition over inheritance**: PASS - The service composes existing stores/readers.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient.
