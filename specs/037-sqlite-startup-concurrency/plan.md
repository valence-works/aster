# Implementation Plan: SQLite Startup Concurrency Hardening

**Branch**: `037-sqlite-startup-concurrency` | **Date**: 2026-06-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/037-sqlite-startup-concurrency/spec.md`

## Summary

Add focused operational hardening coverage for concurrent SQLite JSON provider/schema initialization. The slice verifies concurrent startup is safe for fresh databases, existing tenant-aware databases, and explicit `InitializeSchema = false` no-schema construction. Production changes are not expected unless tests expose a real startup concurrency defect.

## Technical Context

**Language/Version**: C# latest, .NET 8.0 / 9.0 / 10.0 multi-targeting  
**Primary Dependencies**: Existing SQLite JSON provider, Microsoft.Data.Sqlite transitively used by the provider/tests, xUnit test stack; no new dependencies  
**Storage**: Existing SQLite JSON tables only; no schema format changes  
**Testing**: xUnit through `dotnet test Aster.sln`  
**Target Platform**: .NET library / headless SDK  
**Project Type**: SDK/library  
**Performance Goals**: Tests remain bounded to temporary SQLite databases and small fixture data.  
**Constraints**: No new product APIs, storage schema changes, provider registries, public SQL surface, public `IQueryable<Resource>`, query planner behavior, runtime scanning, schedulers, benchmark infrastructure, or dependencies.  
**Scale/Scope**: Test-focused SQLite operational hardening for concurrent provider/schema startup.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **SDK-first/headless**: PASS - Adds provider operational tests for SDK behavior only.
- **Immutable versioning**: PASS - Does not alter resource version semantics.
- **Channel activation**: PASS - Verifies persisted activation state but does not alter activation design.
- **Typed/queryable**: PASS - No query API expansion; no public `IQueryable`.
- **Provider agnostic**: PASS - Scope is provider-specific tests in provider test area; core remains provider-agnostic.
- **Simplicity first**: PASS - Test-first hardening; production changes only if a defect is exposed.
- **Modern C# idioms**: PASS - Uses existing xUnit, async tasks, and async disposal patterns.
- **Readability over cleverness**: PASS - Explicit temporary databases, startup attempts, and final-state assertions.
- **Explicitness over magic**: PASS - Explicit provider options and schema metadata checks.
- **Abstractions justified**: PASS - No new abstractions expected.
- **Optimize for deletion**: PASS - Isolated tests can be removed without product coupling.
- **Composition over inheritance**: PASS - No inheritance hierarchy.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No deployment or runtime setup changes.

## Project Structure

### Documentation (this feature)

```text
specs/037-sqlite-startup-concurrency/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sqlite-startup-concurrency.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
test/Aster.Tests/
└── SqliteJson/
    ├── SqliteJsonSchemaIdempotencyTests.cs
    └── SqliteJsonTenantScopeTests.cs

src/persistence/Aster.Persistence.SqliteJson/
└── SqliteJsonSchema.cs
```

**Structure Decision**: Add focused provider tests under the existing SQLite JSON test namespace, extending the schema idempotency hardening area if that keeps setup reuse simple. Production schema code changes only if tests expose an actual concurrency defect.

## Complexity Tracking

No constitution violations.

## Phase 0 Research Summary

Research will confirm the concurrent startup coverage shape, state assertions, no-schema compatibility coverage, and why this remains a correctness-hardening slice rather than a benchmark or synchronization framework.

## Phase 1 Design Summary

Design will produce research, data model, contract, quickstart, and updated agent context for the test-focused slice.

## Post-Design Constitution Check

- **SDK-first/headless**: PASS - Provider test coverage only.
- **Immutable versioning**: PASS - No version model changes.
- **Channel activation**: PASS - Activation state is verified only.
- **Typed/queryable**: PASS - No query surface changes.
- **Provider agnostic**: PASS - Core remains untouched unless a defect is found outside provider tests.
- **Simplicity first**: PASS - Focused tests, no infrastructure.
- **Modern C# idioms**: PASS - Existing async test patterns.
- **Readability over cleverness**: PASS - Explicit assertions.
- **Explicitness over magic**: PASS - Explicit options and metadata.
- **Abstractions justified**: PASS - No new abstractions.
- **Optimize for deletion**: PASS - Isolated tests.
- **Composition over inheritance**: PASS - No inheritance.
- **Intentional dependencies**: PASS - No new dependencies.
- **Operational simplicity**: PASS - No runtime changes.
