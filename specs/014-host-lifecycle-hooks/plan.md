# Implementation Plan: Host Lifecycle Hooks

**Branch**: `014-host-lifecycle-hooks` | **Date**: 2026-05-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/014-host-lifecycle-hooks/spec.md`

## Summary

Add explicit SDK-local lifecycle hooks for resource saves, activation/deactivation, snapshot export, import preview, and write import. The plan introduces small public hook contracts, immutable operation-specific context records, structured hook outcomes, and a coordinator used by existing core lifecycle services. Hooks are registered explicitly through dependency injection and invoked in deterministic registration order. This slice intentionally avoids recipes, workflow engines, runtime scanning, durable event delivery, live sync, background jobs, provider registries, public SQL, and public `IQueryable<Resource>`.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing core SDK, resource manager/store abstractions, resource schema-version service, portability service, SQLite JSON provider, xUnit test stack; no new dependencies  
**Storage**: Existing resource definitions, resource versions, activation state, and portable snapshots; no schema migration or persisted hook state  
**Testing**: `dotnet test Aster.sln`, focused lifecycle hook tests, portability hook tests, existing lifecycle/portability/provider regression tests, `dotnet build Aster.sln /m:1`, `git diff --check`  
**Target Platform**: .NET SDK/library consumers and local test environment  
**Project Type**: SDK/library with provider packages and tests  
**Performance Goals**: No-hook execution stays equivalent to current behavior aside from a cheap empty-hook dispatch; hook execution is linear in registered hooks for a lifecycle point  
**Constraints**: Explicit DI registration only; deterministic registration-order execution; before hooks can reject before mutation; after hooks observe success and cannot claim rollback; cancellation respected; no recipes, runtime scanning, live sync, background jobs, provider registries, public SQL, or public `IQueryable<Resource>`  
**Scale/Scope**: Core lifecycle and portability hook contracts plus orchestration in existing resource manager, schema upgrade, and portability services; in-memory and SQLite providers require no storage-specific hook implementations

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK/library integration contracts only; no UI, host framework, CMS, or hosted process coupling.
- **Immutable versioning**: PASS - Hooks wrap append-only save paths without mutating historical resource versions.
- **Channel activation**: PASS - Activation/deactivation hooks observe and gate channel state changes while keeping activation separate from resource payloads.
- **Typed/queryable**: PASS - Typed aspects and portable query AST semantics are unchanged; no public `IQueryable` or SQL surface.
- **Provider agnostic**: PASS - Hook orchestration remains in core services and does not depend on SQLite or other provider implementation details.
- **Simplicity first**: PASS - A single hook coordinator plus small context/outcome records satisfies the current integration need without a workflow engine.
- **Modern C# idioms**: PASS - Records, enums, nullable annotations, collection expressions, and async APIs fit the existing SDK style.
- **Readability over cleverness**: PASS - Hook points are explicit before/after calls in existing lifecycle workflows; no metaprogramming or hidden interception.
- **Explicitness over magic**: PASS - Hosts register hooks explicitly; no runtime scanning, attributes, naming conventions, or implicit discovery.
- **Abstractions justified**: PASS - Lifecycle hooks are the demonstrated extension need for host validation, auditing, and policy gates across existing lifecycle operations.
- **Optimize for deletion**: PASS - Hook contracts and coordinator are additive and can be removed without changing provider storage or query contracts.
- **Composition over inheritance**: PASS - Uses composed hook services and immutable context records; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No schema migration, background processing, external service, or deployment change.

## Project Structure

### Documentation (this feature)

```text
specs/014-host-lifecycle-hooks/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- tasks.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- host-lifecycle-hooks.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Abstractions/
|   |   +-- lifecycle hook contracts
|   |   +-- lifecycle hook coordinator contract
|   +-- Models/Lifecycle/
|   |   +-- hook context records
|   |   +-- hook outcome/diagnostic models
|   |   +-- lifecycle point and operation enums
|   +-- Services/
|   |   +-- lifecycle hook coordinator implementation
|   |   +-- resource manager hook integration
|   |   +-- schema upgrade hook integration
|   |   +-- portability hook integration
|   +-- Extensions/
|       +-- DI registration update

test/
+-- Aster.Tests/
    +-- Lifecycle/
    +-- Portability/
    +-- SchemaVersions/
    +-- Services/
```

**Structure Decision**: Keep hook contracts and orchestration in `Aster.Core` because hooks are provider-agnostic SDK behavior around existing core lifecycle services. Avoid provider-specific hook implementations; SQLite JSON and in-memory stores continue to expose the same storage primitives while the core services invoke hooks before and after operations.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use explicit DI-registered hook services over scanning, attributes, or provider registries.
- Use one coordinator service to centralize deterministic ordering, cancellation, and structured failure mapping.
- Model hook contexts as immutable operation-specific records rather than mutable pipeline bags.
- Treat before-hook rejection as pre-mutation failure; do not run later hooks or underlying operations after rejection.
- Treat after-hook failure as visible post-commit failure; do not imply rollback of already-completed operations.
- Keep portability hook diagnostics aligned with existing portability result diagnostics.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/host-lifecycle-hooks.md](contracts/host-lifecycle-hooks.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes SDK contracts only and remains host-agnostic.
- **Immutable versioning**: PASS - Save hooks wrap append-only creates, updates, and schema upgrades without changing versioning rules.
- **Channel activation**: PASS - Activation hooks wrap channel membership operations without moving state into resource payloads.
- **Typed/queryable**: PASS - No query API or typed aspect behavior changes.
- **Provider agnostic**: PASS - Hook invocation lives in core services and does not require provider-specific storage changes.
- **Simplicity first**: PASS - The design avoids recipes, generic workflows, runtime scanning, and durable delivery.
- **Modern C# idioms**: PASS - Records, enums, async APIs, and nullable annotations match existing conventions.
- **Readability over cleverness**: PASS - Explicit hook coordinator calls are easier to review than interception or dynamic dispatch.
- **Explicitness over magic**: PASS - Registration and invocation behavior are visible through DI and service code.
- **Abstractions justified**: PASS - Contracts solve the current host integration need across save, activation, and portability operations.
- **Optimize for deletion**: PASS - Hook model is isolated from storage, querying, and portability data contracts.
- **Composition over inheritance**: PASS - Hook services compose with existing lifecycle services.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflow remains sufficient.
