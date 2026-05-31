# Implementation Plan: Policy Preview Summaries

**Branch**: `027-policy-preview-summaries` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/027-policy-preview-summaries/spec.md`

## Summary

Add pure host-facing summary records and extension helpers over existing `ResourcePolicyEvaluationPreview` objects. The implementation follows the existing policy and restore summary pattern: deterministic candidate, outcome, kind, target, and diagnostic counts; no service registration; no provider/storage changes; and no policy evaluation behavior changes.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK policy preview models, policy diagnostic models, xUnit test stack; no new dependencies
**Storage**: No storage changes. Summaries are pure in-memory views over existing preview result objects; no schema migration, persistence, provider storage, audit records, or physical index changes.
**Testing**: Focused policy preview summary tests, existing policy preview and summary regression tests, `dotnet test Aster.sln`, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: Summary computation is linear over supplied preview candidates and diagnostics and performs no I/O.
**Constraints**: Pure transformations only; deterministic counts; null result inputs fail fast; null candidate/diagnostic collections are treated as empty; no service, storage, provider, query, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, policy evaluation changes, audit persistence, reporting framework, or mutation behavior.
**Scale/Scope**: Core SDK summary records/extensions, focused tests, docs, roadmap, and Spec Kit artifacts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK result helpers only; no UI, CMS, or host authorization behavior.
- **Immutable versioning**: PASS - Summaries are read-only and do not mutate resource versions.
- **Channel activation**: PASS - Activation state remains untouched.
- **Typed/queryable**: PASS - Query AST and typed aspect APIs remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Summaries operate on existing result objects and do not depend on providers.
- **Simplicity first**: PASS - Pure records/extensions are the smallest implementation.
- **Modern C# idioms**: PASS - Records, extension methods, collection expressions, enum ordering, and nullable-safe handling match existing style.
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
specs/027-policy-preview-summaries/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- policy-preview-summaries.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Models/Policies/
|       +-- ResourcePolicyPreviewSummaries.cs
|
+-- persistence/Aster.Persistence.SqliteJson/
    +-- no changes

test/
+-- Aster.Tests/
    +-- Policies/
        +-- PolicyPreviewSummaryTests.cs
```

**Structure Decision**: Keep preview summaries in `Aster.Core.Models.Policies` beside policy preview and application summary models. They are pure SDK helpers over existing result objects and need no service registration or provider implementation.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use pure extension helpers rather than a service.
- Count policy outcomes and policy kinds using deterministic enum ordering.
- Count resource-version targets only when both resource identity and version are present.
- Reuse the existing diagnostic-code count model and helper.
- Treat null collections as empty for host-friendly manually constructed previews.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/policy-preview-summaries.md](contracts/policy-preview-summaries.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Design exposes records and extension helpers only.
- **Immutable versioning**: PASS - No write path is introduced.
- **Channel activation**: PASS - Activation state remains unchanged.
- **Typed/queryable**: PASS - Query APIs remain unchanged.
- **Provider agnostic**: PASS - No provider dependency exists.
- **Simplicity first**: PASS - No service or registry is added.
- **Modern C# idioms**: PASS - Planned code follows established summary patterns.
- **Readability over cleverness**: PASS - Count formulas are direct.
- **Explicitness over magic**: PASS - Callers explicitly invoke `ToSummary`.
- **Abstractions justified**: PASS - No new abstraction is introduced.
- **Optimize for deletion**: PASS - Feature is additive and localized.
- **Composition over inheritance**: PASS - Records/extensions only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing validation commands remain sufficient.
