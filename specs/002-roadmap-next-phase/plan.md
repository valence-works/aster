# Implementation Plan: Persistence & Querying Essentials (Phase 2)

**Branch**: `002-roadmap-next-phase` | **Date**: 2026-03-04 | **Spec**: `/specs/002-roadmap-next-phase/spec.md`
**Input**: Feature specification from `/specs/002-roadmap-next-phase/spec.md`

## Summary

Implement Phase 2 by adding a SQLite + JSON reference persistence provider that preserves immutable resource version history, channel-based activation, and portable query execution semantics from Phase 1 contracts. The plan keeps core logic provider-agnostic by implementing persistence behind existing abstractions (`IResourceWriteStore`, `IResourceQueryService`, `IResourceDefinitionStore`), adds provider-owned infrastructure steps, and validates correctness/performance against a 100k-version dataset.

## Technical Context

**Language/Version**: C# with .NET 8/9/10 multi-targeting in core; ASP.NET Core host on .NET 10  
**Primary Dependencies**: `Microsoft.Extensions.*` abstractions, SQLite ADO.NET provider, `System.Text.Json` for payload serialization  
**Storage**: SQLite database with JSON document storage semantics (JSON text columns plus relational keys/indexes)  
**Testing**: xUnit test suite in `test/Aster.Tests` (unit + integration)  
**Target Platform**: Cross-platform .NET runtime (macOS/Linux/Windows) for library and host execution  
**Project Type**: SDK/library with provider module and sample web host  
**Performance Goals**: Meet spec SC-002 (`>=95%` standard persisted queries under 2 seconds on 100k resource versions)  
**Constraints**: Provider-agnostic core, immutable append-only versions, optimistic concurrency, configurable single/multi-active channels, missing sort values ordered last  
**Scale/Scope**: One production-grade provider (`SQLite + JSON`) supporting lifecycle, querying, infrastructure initialization, and restart durability for 100k resource-version validation dataset

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate

- **I. SDK-First & Headless**: PASS — no UI coupling introduced; feature remains in SDK/provider layers.
- **II. Immutable Versioning**: PASS — append-only resource versions and optimistic locking are explicit requirements.
- **III. Channel-Based Activation**: PASS — per-channel single/multi-active policy retained and persisted.
- **IV. Typed & Queryable**: PASS — existing portable query AST remains the contract; no raw provider query leakage.
- **V. Provider Agnostic**: PASS — implementation uses pluggable store interfaces and provider-owned infrastructure steps.
- **Coding Standards/Governance**: PASS — async + cancellation tokens and nullability remain required; no governance conflicts.

### Post-Design Gate

- **I. SDK-First & Headless**: PASS — design artifacts only add provider implementation + contracts.
- **II. Immutable Versioning**: PASS — data model enforces `(ResourceId, Version)` append-only uniqueness and no in-place mutation.
- **III. Channel-Based Activation**: PASS — activation state modeled separately with channel policy flags.
- **IV. Typed & Queryable**: PASS — contract defines portable operator support and deterministic sort/null behavior.
- **V. Provider Agnostic**: PASS — migration/provisioning contract is abstract; SQLite is reference backend only.

## Project Structure

### Documentation (this feature)

```text
specs/002-roadmap-next-phase/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── persistence-query-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── core/
│   └── Aster.Core/
│       ├── Abstractions/
│       ├── Definitions/
│       ├── Exceptions/
│       ├── Extensions/
│       ├── InMemory/
│       ├── Models/
│       └── Services/
├── apps/
│   └── Aster.Web/
│       ├── Endpoints/
│       ├── Program.cs
│       └── SeedDataInitializer.cs
└── providers/
    └── Aster.Persistence.Sqlite/        # New in Phase 2

test/
└── Aster.Tests/
    ├── Definitions/
    ├── InMemory/
    ├── Integration/
    ├── Services/
    └── Persistence/                     # New in Phase 2
```

**Structure Decision**: Keep `Aster.Core` unchanged as abstraction surface, add a dedicated provider project at `src/providers/Aster.Persistence.Sqlite`, and extend `test/Aster.Tests` with persistence-focused unit/integration coverage to preserve provider-agnostic architecture.

## Phase Plan

### Phase 0 — Research

- Finalize provider design choices for SQLite JSON persistence, query translation boundaries, and infrastructure-step strategy.
- Document trade-offs and rejected alternatives in `research.md`.

### Phase 1 — Design & Contracts

- Define persistence entities and relationships in `data-model.md`.
- Define provider and query behavior contract in `contracts/persistence-query-contract.md`.
- Define verification steps in `quickstart.md` for lifecycle, querying, and infrastructure initialization.

### Phase 2 — Task Planning (next command)

- Break implementation into executable tasks in `tasks.md` via `/speckit.tasks`.

## Complexity Tracking

No constitution violations identified; no exception justification required.
