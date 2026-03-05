# Implementation Plan: Persistence & Querying Essentials (Phase 2)

**Branch**: `002-roadmap-next-phase` | **Date**: 2026-03-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-roadmap-next-phase/spec.md`

## Summary

Implement `Aster.Persistence.Sqlite` — the first production-grade, durable persistence provider for Aster. The provider backs `IResourceDefinitionStore`, `IResourceWriteStore`, and `IResourceQueryService` with Sqlite using JSON document columns for payloads. Phase 2 also adds the `ChannelMode` enum and updates the `IResourceManager.ActivateAsync` signature in `Aster.Core`, upgrades the query contract to translate the portable `ResourceQuery` AST to parameterised SQL at runtime, and extends the test suite with persistence-focused and restart-durability scenarios targeting a 100k resource-version dataset.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 SDK (library multi-targets `net8.0;net9.0;net10.0`)  
**Primary Dependencies**: `Microsoft.Data.Sqlite` (runtime); `System.Text.Json` (serialisation, already in-tree); `xUnit 2.x` (test)  
**Storage**: Sqlite — file-based; path configurable via `SqlitePersistenceOptions`  
**Testing**: xUnit — existing suite in `test/Aster.Tests/`; new persistence tests in `test/Aster.Tests/Persistence/`  
**Target Platform**: Library — runs on any .NET 8/9/10 host; reference app `Aster.Web` targets `net10.0`  
**Project Type**: Library (SDK provider)  
**Performance Goals**: ≥ 95% of standard persisted queries complete in < 2 s against a 100k resource-version dataset (SC-002)  
**Constraints**: Single fixed Sqlite schema version in Phase 2; no in-place migrations; breaking schema change requires fresh database; structured logging only via `ILogger<T>` (no metrics or tracing); slow-query threshold configurable via options (default 500 ms)  
**Scale/Scope**: Validation dataset of 100k `ResourceRecord` rows; all five success criteria (SC-001–SC-005) must be green

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDK-First & Headless | ✅ PASS | `Aster.Persistence.Sqlite` introduces no UI or host-environment dependency. New project lives in `src/persistence/`. |
| II. Immutable Versioning | ✅ PASS | `ResourceRecord` and `ResourceDefinitionRecord` rows are append-only; composite PKs enforce no-overwrite. Optimistic concurrency enforced on `UpdateAsync` and `ActivateAsync`. |
| III. Channel-Based Activation | ✅ PASS | `ActivationRecord` key is `(ResourceId, Channel)`. Per-channel `ChannelMode` durable policy satisfies multi-channel parallelism. |
| IV. Typed & Queryable | ✅ PASS | `ResourceQuery` AST is the public contract; provider translates to parameterised Sqlite SQL internally. No `IQueryable` or raw SQL leaks across abstraction boundary. |
| V. Provider Agnostic | ✅ PASS | New `Aster.Persistence.Sqlite` project depends on `Aster.Core` abstractions only. No Sqlite/DB types cross abstraction boundary into `Aster.Core`. |
| Coding Standards | ✅ PASS | All public APIs use `CancellationToken`, `Async` suffix, file-scoped namespaces, nullability enabled. Options type exposes slow-query threshold. |
| Semantic Versioning | ✅ PASS | `ChannelMode` addition + `ActivateAsync` signature change = MINOR bump (additive; `bool allowMultipleActive` replaced by `ChannelMode? mode` with documented migration path). |
| Architecture Reviews | ⚠️ REQUIRED | Phase 1 → Phase 2 is a major phase transition. An architecture review document must be created in `docs/` before merging to `main` (per constitution §Governance). |

> **Gate verdict: PASS with obligation.** Architecture review document (`docs/architecture-review-phase2.md`) must be authored before the branch merges. All other gates pass unconditionally.

## Project Structure

### Documentation (this feature)

```text
specs/002-roadmap-next-phase/
├── plan.md                           ← this file
├── research.md                       ← Phase 0 complete
├── data-model.md                     ← Phase 1 complete
├── quickstart.md                     ← Phase 1 complete
├── contracts/
│   └── persistence-query-contract.md ← Phase 1 complete
└── tasks.md                          ← Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── core/
│   └── Aster.Core/                         # existing — Phase 2 adds ChannelMode + ActivateAsync sig change
│       ├── Models/
│       │   └── Instances/
│       │       └── ActivationState.cs      # add Mode property (ChannelMode enum)
│       ├── Abstractions/
│       │   └── IResourceManager.cs         # replace bool allowMultipleActive → ChannelMode? mode
│       └── InMemory/
│           └── InMemoryResourceManager.cs  # update ActivateAsync impl for ChannelMode
├── persistence/
│   └── Aster.Persistence.Sqlite/           # NEW project — see §Provider Naming Convention
│       ├── Aster.Persistence.Sqlite.csproj
│       ├── SqlitePersistenceOptions.cs
│       ├── Extensions/
│       │   └── ServiceCollectionExtensions.cs
│       ├── Persistence/
│       │   ├── SqliteResourceDefinitionStore.cs
│       │   ├── SqliteResourceWriteStore.cs
│       │   └── SqliteResourceQueryService.cs
│       ├── Schema/
│       │   └── SchemaInitializer.cs
│       └── Internal/
│           ├── SqliteQueryTranslator.cs
│           └── JsonSerializerOptions.cs
└── apps/
    └── Aster.Web/                          # update DI registration to use Sqlite provider

test/
└── Aster.Tests/
    ├── Persistence/                        # NEW — Sqlite provider tests
    │   ├── SqliteDefinitionStoreTests.cs
    │   ├── SqliteResourceWriteStoreTests.cs
    │   ├── SqliteActivationTests.cs
    │   ├── SqliteQueryServiceTests.cs
    │   ├── RestartDurabilityTests.cs       # covers SC-001 and SC-005
    │   └── PerformanceTests.cs             # covers SC-002 (100k dataset)
    └── Integration/
        └── QuickstartIntegrationTest.cs    # existing — update for ChannelMode

docs/
└── architecture-review-phase2.md           # REQUIRED before merge (constitution gate)
```

**Structure Decision**: Single-project provider pattern (`Aster.Persistence.Sqlite` under `src/persistence/`). Persistence providers are separated from core to keep `src/core/` free of provider-specific infrastructure. Each core abstraction (`IResourceDefinitionStore`, `IResourceWriteStore`, `IResourceQueryService`) maps to a dedicated class inside `Persistence/`. Internal SQL translation lives in `Internal/` to keep the boundary clean. All three implementations are registered atomically via a single `AddSqlitePersistence(options)` extension method. No separate model project is required — persistence record shapes are lightweight private types defined within `Aster.Persistence.Sqlite.Persistence`.

## Provider Naming Convention

All persistence provider projects follow the pattern `Aster.Persistence.[ProviderName]`, placed under `src/persistence/`.

| Project | Description |
|---------|-------------|
| `Aster.Persistence.Sqlite` | Sqlite + JSON reference provider (this phase) |
| `Aster.Persistence.SqlServer` | Future: SQL Server via `Microsoft.Data.SqlClient` directly |
| `Aster.Persistence.EFCore` | Future: EF Core abstraction as the provider — targets any EF-supported backend (SQL Server, PostgreSQL, etc.) via EF's own provider system |

**Notes**:
- `Aster.Persistence.EFCore` is intentionally named for the *abstraction layer* (EF Core), not the underlying database. It would accept an `IServiceCollection` already configured with a `DbContext`, making the database choice orthogonal.
- Provider namespaces mirror project names: `Aster.Persistence.Sqlite`, `Aster.Persistence.SqlServer`, etc.
- DI extension naming follows `Add{ProviderName}Persistence()` (e.g., `AddSqlitePersistence()`, `AddSqlServerPersistence()`).
- Options classes follow `{ProviderName}PersistenceOptions` (e.g., `SqlitePersistenceOptions`, `SqlServerPersistenceOptions`).

## Data Access Strategy

**Decision: Raw ADO.NET (`Microsoft.Data.Sqlite`) for `Aster.Persistence.Sqlite`.**

| Option | Assessment |
|--------|------------|
| **Raw ADO.NET** ✅ | Lowest dependency surface; full control over connection/transaction lifecycle; no ORM types can leak across abstraction boundaries; perfectly adequate for 3 simple tables with opaque JSON payload columns. The "hard part" — `ResourceQuery` AST → SQL translation — is custom regardless of data access layer. |
| **Dapper** ⚠️ | Thin micro-ORM; saves result-set mapping boilerplate but adds a dependency for minimal gain on this schema. Worth revisiting if a future provider has significantly more complex joins. |
| **EF Core** ❌ (as internal implementation) | Brings migrations, change tracking, and a LINQ provider. Migrations conflict with the single-fixed-schema-version constraint. `IQueryable` leakage risk violates Constitution Principle IV. Correct fit only as `Aster.Persistence.EFCore` — a dedicated provider for teams whose stack already includes EF Core. |

For future providers: apply the same reasoning. Use raw ADO.NET (`Microsoft.Data.SqlClient`) for `Aster.Persistence.SqlServer`. Introduce Dapper only if result-set mapping becomes genuinely burdensome (e.g., many non-JSON columns). Reserve `Aster.Persistence.EFCore` for teams that want EF Core as the provider layer itself.

## Complexity Tracking

> No constitution violations. All design choices align with existing principles without unjustified complexity.
