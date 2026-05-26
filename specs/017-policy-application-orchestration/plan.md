# Implementation Plan: Policy Application Orchestration

**Branch**: `017-policy-application-orchestration` | **Date**: 2026-05-27 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/017-policy-application-orchestration/spec.md`

## Summary

Add host-controlled policy application orchestration for selected archive and soft-delete preview candidates. The implementation adds a small core SDK application service and request/result models that validate candidate shape, stale resource versions, current policy declaration compatibility, same-resource lifecycle conflicts, tenant boundaries, and pruning preview-only behavior before delegating supported marker writes to the existing lifecycle marker service. The slice does not add provider storage, schedulers, hidden execution, destructive pruning writes, lifecycle hook behavior, provider registries, public SQL, or public `IQueryable<Resource>`.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, policy declaration/preview models, lifecycle marker service/store, resource definition store, resource version reader, in-memory store, SQLite JSON provider through existing abstractions, xUnit test stack; no new dependencies  
**Storage**: No schema or storage changes. Application orchestration writes only existing lifecycle marker state through `IResourceLifecycleMarkerService`; definitions, resources, activation state, portability snapshots, and SQLite tables remain unchanged.  
**Testing**: `dotnet test Aster.sln`, focused policy application tests, tenant-scoped application tests, lifecycle marker regression tests, SQLite JSON compatibility tests through existing provider registration, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: Application work is bounded by submitted candidates; the service reads latest resource versions and current definitions only for candidate resources in the effective tenant; duplicate/conflict preflight avoids unnecessary marker writes.  
**Constraints**: Hosts explicitly submit selected candidates; archive and soft-delete are the only write-side outcomes; pruning remains preview-only; stale candidate versions fail; same-resource conflicting lifecycle outcomes in one request all fail before either marker is applied; referenced policy declarations must still exist and match the requested outcome; no lifecycle hook behavior is added; no new dependencies, provider registries, runtime scanning, public SQL, public `IQueryable<Resource>`, schedulers, hidden jobs, restore workflows, or destructive pruning writes.  
**Scale/Scope**: Core SDK models/service/DI/docs plus focused tests over in-memory and SQLite-backed registrations; no provider-specific implementation beyond compatibility coverage.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library contracts only; no UI, CMS, host framework, scheduler, or authorization coupling.
- **Immutable versioning**: PASS - Application writes lifecycle markers only and never rewrites resource versions.
- **Channel activation**: PASS - Activation state is unaffected by application and remains separate from resource payloads.
- **Typed/queryable**: PASS - Query semantics are unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Core service depends on existing definition, version reader, and marker abstractions rather than provider-specific storage.
- **Simplicity first**: PASS - A direct application service over explicit candidates satisfies current requirements without a policy engine or registry.
- **Modern C# idioms**: PASS - Records, collection expressions, nullable-safe request models, and async APIs match existing SDK style.
- **Readability over cleverness**: PASS - Candidate validation, conflict preflight, and marker application are direct service steps.
- **Explicitness over magic**: PASS - Hosts submit candidates and timestamps explicitly; no discovery, ambient execution, or background behavior is added.
- **Abstractions justified**: PASS - A host-facing application contract is needed because application is distinct from preview and direct marker writes.
- **Optimize for deletion**: PASS - Models and service are additive and can be removed without changing providers or resource payloads.
- **Composition over inheritance**: PASS - Uses composed services and data records; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient; no worker, migration, or external service is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/017-policy-application-orchestration/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- policy-application-orchestration.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- policy application service contract
|   +-- Models/Policies/
|   |   +-- policy application request/result models and diagnostic codes
|   +-- Services/
|   |   +-- default policy application service
|   +-- Extensions/
|       +-- AddAsterCore registration for the application service
|
+-- persistence/Aster.Persistence.SqliteJson/
|   +-- no provider-specific storage changes; compatibility tests use existing registrations
|
+-- apps/Aster.Web/
    +-- no feature-specific host UI

test/
+-- Aster.Tests/
    +-- Policies/
    +-- Tenancy/
    +-- SqliteJson/
```

**Structure Decision**: Keep orchestration in `Aster.Core` because it is SDK policy behavior over existing provider abstractions. Providers do not need new contracts or storage because lifecycle marker persistence already exists. Do not add a policy engine package, scheduler, hook pipeline, registry, provider-specific executor, or new dependency.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Add a small host-facing policy application service instead of extending preview or direct marker writes.
- Use explicit application candidate records derived from preview candidates.
- Allow partial batch success with one result per candidate.
- Preflight same-resource lifecycle conflicts before marker writes.
- Fail stale resource-version candidates by comparing with latest resource versions in the effective tenant.
- Require current policy declarations to exist and match the submitted lifecycle outcome.
- Keep pruning writes, lifecycle hook behavior, provider registries, and scheduler behavior out of scope.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/policy-application-orchestration.md](contracts/policy-application-orchestration.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK contracts only and leaves scheduling, compliance decisions, and authorization to hosts.
- **Immutable versioning**: PASS - Application delegates to lifecycle marker writes and never rewrites resource versions.
- **Channel activation**: PASS - Activation is neither read as side effect nor mutated by application.
- **Typed/queryable**: PASS - No query model changes, raw SQL, or public `IQueryable` are introduced.
- **Provider agnostic**: PASS - Service depends on existing core abstractions and works with in-memory/SQLite registrations through DI.
- **Simplicity first**: PASS - Direct validation and marker delegation meet current needs without broad execution infrastructure.
- **Modern C# idioms**: PASS - Records and async service contracts fit existing code.
- **Readability over cleverness**: PASS - Request/result models make every candidate outcome inspectable.
- **Explicitness over magic**: PASS - Application only happens through host-submitted candidates.
- **Abstractions justified**: PASS - The single new application contract is a demonstrated host workflow between preview and marker writes.
- **Optimize for deletion**: PASS - Removing this feature would remove additive models/service/registration only.
- **Composition over inheritance**: PASS - The service composes existing validator/reader/marker services.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No background worker, migration framework, external service, or deployment-time runner is introduced.
