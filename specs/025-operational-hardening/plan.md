# Implementation Plan: Operational Hardening

**Branch**: `025-operational-hardening` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/025-operational-hardening/spec.md`

## Summary

Add targeted operational hardening coverage around retry and concurrency-sensitive Phase 5 workflows: lifecycle restore, policy pruning application, and repeated historical activation. The slice is test-first and does not add product APIs, storage schema changes, provider registries, background jobs, schedulers, benchmark infrastructure, or dependencies. If tests expose a bug, fixes must be narrowly scoped to existing behavior.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting
**Primary Dependencies**: Existing core SDK, in-memory store, SQLite JSON provider, lifecycle restore service, policy pruning application service, historical activation path, xUnit test stack; no new dependencies
**Storage**: Existing resource version, activation state, and lifecycle marker storage only. No schema migration, data rewrite, persisted test artifacts, or physical index changes.
**Testing**: `dotnet test Aster.sln`, focused operational hardening tests, existing lifecycle/policy/versioning/SQLite regression tests, `dotnet build Aster.sln /m:1`, `git diff --check`
**Target Platform**: .NET SDK/library consumers and local test environment
**Project Type**: SDK/library with provider packages and tests
**Performance Goals**: Hardening tests remain bounded to small explicit fixtures and do not introduce benchmark infrastructure.
**Constraints**: Test-focused slice; deterministic explicit resources/tenants/channels; final state assertions required; no new product APIs, storage schema changes, provider registries, public SQL, public `IQueryable<Resource>`, runtime scanning, automatic discovery, schedulers, background jobs, benchmark infrastructure, or dependencies.
**Scale/Scope**: One focused operational test file, possible narrow bug fix if needed, roadmap/agent housekeeping, and Spec Kit artifacts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds tests and docs only unless a narrow SDK bug is exposed.
- **Immutable versioning**: PASS - Hardening asserts version pruning and activation invariants without rewriting versions.
- **Channel activation**: PASS - Activation state remains separate; tests verify repeat activation behavior.
- **Typed/queryable**: PASS - Query APIs remain unchanged; no public SQL or `IQueryable` is introduced.
- **Provider agnostic**: PASS - Core behavior remains provider-agnostic; SQLite tests validate provider parity through existing abstractions.
- **Simplicity first**: PASS - Focused regression tests are simpler than infrastructure or framework changes.
- **Modern C# idioms**: PASS - Tests use existing xUnit async patterns and collection expressions.
- **Readability over cleverness**: PASS - Scenarios are explicit and state-based.
- **Explicitness over magic**: PASS - Fixtures name resource IDs, candidates, tenants, and channels explicitly.
- **Abstractions justified**: PASS - No new abstraction is planned.
- **Optimize for deletion**: PASS - Tests and docs can be removed without runtime cleanup.
- **Composition over inheritance**: PASS - No inheritance changes.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No runtime setup, storage, worker, or deployment change.

## Project Structure

### Documentation (this feature)

```text
specs/025-operational-hardening/
+-- spec.md
+-- plan.md
+-- research.md
+-- data-model.md
+-- quickstart.md
+-- checklists/
|   +-- requirements.md
+-- contracts/
    +-- operational-hardening.md
```

### Source Code (repository root)

```text
test/
+-- Aster.Tests/
    +-- Operational/
        +-- OperationalHardeningTests.cs

src/
+-- core/Aster.Core/
|   +-- no planned product changes
+-- persistence/Aster.Persistence.SqliteJson/
    +-- no planned provider changes
```

**Structure Decision**: Place hardening coverage under `test/Aster.Tests/Operational/` because these scenarios cross lifecycle, policy, versioning, and provider boundaries. Keep production code untouched unless a test exposes a real existing-behavior bug.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research decisions are captured in [research.md](research.md). Key outcomes:

- Use focused xUnit regression tests, not benchmark or stress infrastructure.
- Cover both retry and one bounded concurrent restore scenario.
- Cover pruning retry in memory and persisted SQLite state.
- Cover repeated historical activation for single-active and multi-active modes.
- Keep roadmap housekeeping in the same feature PR.

## Phase 1 Design Summary

Design artifacts:

- [data-model.md](data-model.md)
- [contracts/operational-hardening.md](contracts/operational-hardening.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - No UI or host-specific runtime is introduced.
- **Immutable versioning**: PASS - Tests assert immutable/latest invariants.
- **Channel activation**: PASS - Tests assert activation state behavior through existing manager APIs.
- **Typed/queryable**: PASS - Query surface remains unchanged.
- **Provider agnostic**: PASS - Provider parity is validated through existing abstractions.
- **Simplicity first**: PASS - No new infrastructure or framework is introduced.
- **Modern C# idioms**: PASS - Uses direct async xUnit tests.
- **Readability over cleverness**: PASS - Tests are explicit and state-based.
- **Explicitness over magic**: PASS - No discovery or ambient state.
- **Abstractions justified**: PASS - No new abstraction.
- **Optimize for deletion**: PASS - Additive tests and docs only.
- **Composition over inheritance**: PASS - No inheritance.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - Existing commands validate the slice.
