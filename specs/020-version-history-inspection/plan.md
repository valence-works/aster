# Implementation Plan: Resource Version History Inspection

**Branch**: `020-version-history-inspection` | **Date**: 2026-05-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/020-version-history-inspection/spec.md`

## Summary

Add a read-only, host-facing resource version history inspection service. The implementation composes existing resource version reads, lifecycle marker reads, and a narrow activation-state reader so hosts can inspect one tenant-scoped resource timeline with latest, draft, active-channel, lifecycle, and conservative maintenance signals. The feature adds no storage schema changes, no mutation behavior, no public SQL, no public `IQueryable<Resource>`, no provider registry, no scheduler, and no query planner.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK, resource version reader, lifecycle marker store, in-memory store, SQLite JSON provider, xUnit test stack; no new dependencies
**Storage**: Existing resource versions, activation state, and lifecycle marker storage only. No schema migration, data rewrite, portability snapshot format change, or physical index creation.
**Testing**: `dotnet test Aster.sln`, focused version history tests, tenant isolation tests, SQLite JSON parity tests, existing policy/restore/pruning/query/portability regression tests, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: History inspection is bounded to one resource ID in one effective tenant and should use existing scoped reads rather than scanning unrelated tenants.
**Constraints**: Read-only behavior; one effective tenant per request; deterministic version ordering; active channels must be enumerated for the requested resource; lifecycle state is resource-level current marker state; maintenance hints are conservative and not policy eligibility decisions; no scheduler, authorization engine, provider registry, runtime scanning, public SQL, public `IQueryable<Resource>`, broad workflow/state-machine infrastructure, or schema migration.
**Scale/Scope**: Core SDK request/result models, one history service, a narrow activation-state reader contract implemented by in-memory and SQLite JSON stores, DI registration, docs, and focused tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds host-facing SDK contracts only; no UI, CMS, or host authorization behavior.
- **Immutable versioning**: PASS - Inspection is read-only and does not mutate resource versions.
- **Channel activation**: PASS - Activation remains separate from resource payloads; history only reports active channels.
- **Typed/queryable**: PASS - Existing typed aspects and query AST remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Core service composes abstractions; provider-specific reads remain in provider packages.
- **Simplicity first**: PASS - One read-only service plus one narrow state reader is simpler than a query planner, registry, or reporting framework.
- **Modern C# idioms**: PASS - Records, collection expressions, nullable-safe request handling, async APIs, and pattern matching fit existing style.
- **Readability over cleverness**: PASS - History assembly is an explicit orchestration over versions, activation states, and marker state.
- **Explicitness over magic**: PASS - Hosts submit resource ID and tenant scope explicitly; no scanning or ambient tenant discovery.
- **Abstractions justified**: PASS - Active channel enumeration is not available through existing public reads; a narrow reader solves a demonstrated current need.
- **Optimize for deletion**: PASS - Models, service, and reader contract are additive and localized.
- **Composition over inheritance**: PASS - Uses services, records, and explicit collaborators; no new inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No migration, worker, external service, or setup change.

## Project Structure

### Documentation (this feature)

```text
specs/020-version-history-inspection/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- version-history-inspection.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- version history service and activation-state reader contracts
|   +-- Models/Instances/
|   |   +-- version history request/result models
|   +-- Services/
|       +-- default version history inspection service
|
+-- persistence/Aster.Persistence.SqliteJson/
|   +-- activation-state reader implementation over existing activation storage
|
+-- apps/Aster.Web/
    +-- no feature-specific host UI

test/
+-- Aster.Tests/
    +-- Versioning/
    +-- Tenancy/
    +-- SqliteJson/
```

**Structure Decision**: Keep history inspection in `Aster.Core` because version state, tenant boundaries, lifecycle marker visibility, and maintenance hints are SDK semantics. Providers only expose activation-state reads needed to enumerate active channels from existing storage. Do not add a provider registry, scheduler, reporting database, query planner, authorization layer, migration framework, or new dependency.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Add a dedicated read-only history service rather than expanding policy or query services.
- Add a narrow activation-state reader because current active-version reads require a known channel.
- Treat maintenance hints as conservative visibility signals, not policy eligibility.
- Preserve tenant-scoped missing-resource behavior by returning an empty result.
- Keep SQLite behavior provider-specific and schema-free.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/version-history-inspection.md](contracts/version-history-inspection.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK services and models only.
- **Immutable versioning**: PASS - No write path is introduced.
- **Channel activation**: PASS - Active channels are read from activation state and remain decoupled from resource payloads.
- **Typed/queryable**: PASS - Query AST and typed aspect APIs remain unchanged.
- **Provider agnostic**: PASS - Core service depends on reader abstractions; SQLite-specific SQL stays in the provider package.
- **Simplicity first**: PASS - Scope remains one resource history call rather than a reporting framework.
- **Modern C# idioms**: PASS - Planned models and service follow existing modern C# style.
- **Readability over cleverness**: PASS - Summaries expose direct booleans and channel lists without hidden inference.
- **Explicitness over magic**: PASS - Tenant scope and resource ID are explicit request fields.
- **Abstractions justified**: PASS - The activation reader is required to enumerate channels and is limited to that need.
- **Optimize for deletion**: PASS - Removing the feature would remove additive contracts/models/service and provider reader methods.
- **Composition over inheritance**: PASS - The service composes existing stores/readers.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient.
