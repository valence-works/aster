# Implementation Plan: Policy Application Summaries

**Branch**: `022-policy-application-summaries` | **Date**: 2026-05-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/022-policy-application-summaries/spec.md`

## Summary

Add small host-facing summary records and pure aggregation helpers over existing policy application and policy pruning application result types. The implementation gives hosts deterministic status counts, completion booleans, affected target counts, and diagnostic-code counts for UI/reporting without changing policy execution, storage, providers, or operational behavior.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK policy result models and xUnit test stack; no new dependencies
**Storage**: None. Summaries are pure in-memory views over existing result objects; no schema migration, persistence, audit records, or provider storage changes.
**Testing**: Focused policy summary tests, `dotnet test Aster.sln`, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers
**Project Type**: SDK/library
**Performance Goals**: Summary generation is linear in candidate and diagnostic count for one result object.
**Constraints**: No policy re-evaluation, no writes, no stores, no query providers, no lifecycle hooks, no scheduler, no authorization engine, no provider registry, no runtime scanning, no public SQL, no public `IQueryable<Resource>`, no audit persistence, and no broad reporting framework.
**Scale/Scope**: Two existing result families: marker-based policy application and version-pruning policy application.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds SDK result helpers only; no UI or host framework coupling.
- **Immutable versioning**: PASS - No resource versions are created, mutated, or removed.
- **Channel activation**: PASS - Activation state is not touched.
- **Typed/queryable**: PASS - Query AST and typed aspects are unchanged; no SQL or `IQueryable` surface is introduced.
- **Provider agnostic**: PASS - Summaries do not depend on providers.
- **Simplicity first**: PASS - Pure summary records and static/extension helpers are simpler than a reporting service.
- **Modern C# idioms**: PASS - Use records, collection expressions, and LINQ where they improve clarity.
- **Readability over cleverness**: PASS - Aggregation rules remain explicit and test-driven.
- **Explicitness over magic**: PASS - Hosts call summary helpers directly.
- **Abstractions justified**: PASS - No service abstraction or framework is introduced.
- **Optimize for deletion**: PASS - Summary types are local to policy result models and easy to remove.
- **Composition over inheritance**: PASS - Data records and helpers only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No deployment, storage, worker, or provider setup change.

## Project Structure

### Documentation (this feature)

```text
specs/022-policy-application-summaries/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- policy-application-summaries.md
```

### Source Code (repository root)

```text
src/
+-- core/Aster.Core/
|   +-- Models/Policies/
|   |   +-- ResourcePolicyApplication.cs
|   |   +-- ResourcePolicyPruningApplication.cs
|   |   +-- ResourcePolicyApplicationSummaries.cs
|   +-- README.md

test/
+-- Aster.Tests/
    +-- Policies/
        +-- PolicyApplicationSummaryTests.cs
```

**Structure Decision**: Keep summaries alongside policy result models in core. Do not add services, DI registration, persistence contracts, provider behavior, or reporting infrastructure.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use pure result-to-summary helpers instead of a service.
- Keep one shared diagnostic count model.
- Treat skipped candidates as not failed but not fully successful.
- Count affected resources/targets only for successful statuses.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/policy-application-summaries.md](contracts/policy-application-summaries.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Public SDK models only.
- **Immutable versioning**: PASS - No version state changes.
- **Channel activation**: PASS - No activation changes.
- **Typed/queryable**: PASS - No query surface change.
- **Provider agnostic**: PASS - Provider-free transformation.
- **Simplicity first**: PASS - Direct helpers over existing results.
- **Modern C# idioms**: PASS - Records and simple aggregation.
- **Readability over cleverness**: PASS - Named properties document semantics.
- **Explicitness over magic**: PASS - No implicit hooks or scanners.
- **Abstractions justified**: PASS - No new service abstraction.
- **Optimize for deletion**: PASS - Local additive result helpers.
- **Composition over inheritance**: PASS - Records and extension methods only.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing build/test workflow is sufficient.
