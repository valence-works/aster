# Implementation Plan: Historical Version Activation

**Branch**: `021-historical-version-activation` | **Date**: 2026-05-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/021-historical-version-activation/spec.md`

## Summary

Allow hosts to activate any existing resource version, including historical non-latest versions, through the existing activation APIs. The implementation removes the latest-only activation restriction while preserving version existence checks, tenant scoping, lifecycle hooks, single-active/multi-active behavior, deterministic active-version ordering, and append-only resource version storage. No new service, provider contract, schema change, registry, scheduler, authorization engine, public SQL, or public queryable resource surface is introduced.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK, `IResourceManager`, resource version reader/writer, in-memory store, SQLite JSON provider through existing abstractions, lifecycle hook dispatcher, xUnit test stack; no new dependencies
**Storage**: Existing activation state storage only. Historical activation updates activation rows or in-memory activation state; no resource version rewrite, no latest-version change, no lifecycle marker mutation, no portability format change, and no schema migration.
**Testing**: `dotnet test Aster.sln`, focused activation tests, tenant historical activation tests, lifecycle hook activation tests, SQLite activation persistence tests, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: Activation remains bounded to one resource ID, one version number, and one channel in one effective tenant.
**Constraints**: Version must exist in the effective tenant; latest remains unchanged; activation state remains separate from resource payloads; existing hook behavior must remain observable; no scheduler, authorization engine, provider registry, runtime scanning, public SQL, public `IQueryable<Resource>`, broad workflow/state-machine infrastructure, or schema migration.
**Scale/Scope**: Core activation semantics in provider-backed and direct in-memory managers, docs, Spec Kit artifacts, and focused tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Changes SDK activation behavior only; no UI or host framework coupling.
- **Immutable versioning**: PASS - No resource versions are created or mutated.
- **Channel activation**: PASS - Activation remains separate state keyed by channel.
- **Typed/queryable**: PASS - Typed aspects and query AST semantics remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Uses existing version reader/writer abstractions.
- **Simplicity first**: PASS - Updating existing activation semantics is simpler than adding a promotion service.
- **Modern C# idioms**: PASS - Planned edits keep existing async/record/collection style.
- **Readability over cleverness**: PASS - The behavior is a direct validation change with explicit tests.
- **Explicitness over magic**: PASS - Hosts explicitly request the version and channel to activate.
- **Abstractions justified**: PASS - No new abstraction is introduced.
- **Optimize for deletion**: PASS - The feature is a localized behavior change and tests/docs.
- **Composition over inheritance**: PASS - No inheritance changes.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No schema, migration, worker, or deployment change.

## Project Structure

### Documentation (this feature)

```text
specs/021-historical-version-activation/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- historical-version-activation.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Services/DefaultResourceManager.cs
|   +-- InMemory/InMemoryResourceManager.cs
|   +-- Abstractions/IResourceManager.cs
|   +-- README.md

test/
+-- Aster.Tests/
    +-- InMemory/
    +-- Integration/
    +-- Lifecycle/
    +-- SqliteJson/
    +-- Tenancy/
```

**Structure Decision**: Keep this as a behavior change in existing activation APIs. Do not add a new promotion service, provider capability, workflow engine, or storage shape.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Reuse existing `ActivateAsync` APIs and remove the latest-only check.
- Preserve version existence validation.
- Preserve `allowMultipleActive` semantics.
- Preserve lifecycle hook invocation shape.
- Keep provider storage unchanged.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/historical-version-activation.md](contracts/historical-version-activation.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Existing SDK APIs remain the surface.
- **Immutable versioning**: PASS - Activation writes do not alter resource snapshots.
- **Channel activation**: PASS - Activation records remain the only mutated state.
- **Typed/queryable**: PASS - Query and typed aspect behavior is unchanged.
- **Provider agnostic**: PASS - No provider-specific code path is added.
- **Simplicity first**: PASS - The design removes an overly narrow validation rule.
- **Modern C# idioms**: PASS - Existing idioms are retained.
- **Readability over cleverness**: PASS - Tests document the changed semantics directly.
- **Explicitness over magic**: PASS - No ambient behavior is added.
- **Abstractions justified**: PASS - No new abstraction.
- **Optimize for deletion**: PASS - Localized edits remain easy to revert if needed.
- **Composition over inheritance**: PASS - No inheritance changes.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflows remain sufficient.
