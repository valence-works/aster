# Implementation Plan: Lifecycle Restore Summaries

**Branch**: `026-lifecycle-restore-summaries` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/026-lifecycle-restore-summaries/spec.md`

## Summary

Add pure host-facing summary records and extension helpers over existing lifecycle restore preview and application result objects. The implementation mirrors the policy application summary pattern: deterministic status counts, distinct resource counts, diagnostic code counts, no service registration, no provider/storage changes, and no restore behavior changes.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK lifecycle restore models, policy diagnostic models, xUnit test stack; no new dependencies
**Storage**: No storage changes. Summaries are pure in-memory views over existing result objects; no schema migration, persistence, provider storage, audit records, or physical index changes.
**Testing**: Focused lifecycle restore summary tests, existing lifecycle restore and policy summary regression tests, `dotnet test Aster.sln`, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: Summary computation is linear over supplied candidate results and performs no I/O.
**Constraints**: Pure transformations only; deterministic counts; null result inputs fail fast; null candidate/diagnostic collections are treated as empty; no service, storage, provider, query, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, policy evaluation, audit persistence, reporting framework, or mutation behavior.
**Scale/Scope**: Core SDK summary records/extensions, focused tests, docs, roadmap, and Spec Kit artifacts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK result helpers only; no UI, CMS, or host authorization behavior.
- **Immutable versioning**: PASS - Summaries are read-only and do not mutate resource versions.
- **Channel activation**: PASS - Activation state remains untouched.
- **Typed/queryable**: PASS - Query AST and typed aspect APIs remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Summaries operate on existing result objects and do not depend on providers.
- **Simplicity first**: PASS - Pure records/extensions are the smallest implementation.
- **Modern C# idioms**: PASS - Records, extension methods, collection expressions, and nullable-safe handling match existing style.
- **Readability over cleverness**: PASS - Counts are direct and explicit.
- **Explicitness over magic**: PASS - Hosts explicitly call summary helpers on result objects.
- **Abstractions justified**: PASS - No service or new interface is introduced.
- **Optimize for deletion**: PASS - Removing the feature removes one additive model file, tests, and docs.
- **Composition over inheritance**: PASS - Uses records and extension methods; no inheritance hierarchy.
- **Intentional dependencies**: PASS - No new third-party dependencies.
- **Operational simplicity**: PASS - No migration, worker, external service, or setup change.

## Project Structure

### Documentation (this feature)

```text
specs/026-lifecycle-restore-summaries/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- lifecycle-restore-summaries.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Models/Instances/
|       +-- ResourceLifecycleRestoreSummaries.cs
|
+-- persistence/Aster.Persistence.SqliteJson/
    +-- no changes

test/
+-- Aster.Tests/
    +-- Lifecycle/
        +-- ResourceLifecycleRestoreSummaryTests.cs
```

**Structure Decision**: Keep restore summaries in `Aster.Core.Models.Instances` beside lifecycle restore models. They are pure SDK helpers over existing result objects and need no service registration or provider implementation.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use pure extension helpers rather than a service.
- Mirror the existing policy application summary shape where the restore domain has equivalent concepts.
- Count successful terminal statuses differently for preview and application.
- Treat null collections as empty for host-friendly manually constructed results.
- Keep diagnostic counts deterministic and shared with policy diagnostics.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/lifecycle-restore-summaries.md](contracts/lifecycle-restore-summaries.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes records and extension helpers only.
- **Immutable versioning**: PASS - No write path is introduced.
- **Channel activation**: PASS - Activation state remains unchanged.
- **Typed/queryable**: PASS - Query APIs remain unchanged.
- **Provider agnostic**: PASS - No provider dependency exists.
- **Simplicity first**: PASS - No service or registry is added.
- **Modern C# idioms**: PASS - Planned code follows the policy summary pattern.
- **Readability over cleverness**: PASS - Count formulas are direct.
- **Explicitness over magic**: PASS - Callers explicitly invoke `ToSummary`.
- **Abstractions justified**: PASS - No new abstraction is introduced.
- **Optimize for deletion**: PASS - Feature is additive and localized.
- **Composition over inheritance**: PASS - Records/extensions only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing validation commands remain sufficient.
