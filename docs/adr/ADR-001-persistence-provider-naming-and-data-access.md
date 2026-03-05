# ADR-001: Persistence Provider Naming Convention and Data Access Strategy

**Date**: 2026-03-05  
**Status**: Accepted  
**Deciders**: Aster core team  
**Phase**: Phase 2 — Persistence & Querying Essentials  

---

## Context

Phase 2 introduces the first production-grade persistence provider (`Aster.Persistence.Sqlite`). Two decisions needed to be recorded explicitly before implementation:

1. **How persistence provider projects are named and located** — to ensure all current and future providers are predictably discoverable and consistently structured.
2. **Which data access strategy is used inside a provider** — to ensure the chosen approach does not violate Constitution Principles IV (Typed & Queryable) or V (Provider Agnostic).

---

## Decision 1: Provider Naming Convention

### Decision

All persistence provider projects follow the scheme `Aster.Persistence.[ProviderName]` and are placed under `src/persistence/`.

| Project | Target scenario |
|---------|----------------|
| `Aster.Persistence.Sqlite` | Sqlite + JSON columns — reference provider (Phase 2) |
| `Aster.Persistence.SqlServer` | SQL Server via `Microsoft.Data.SqlClient` directly — future |
| `Aster.Persistence.EFCore` | EF Core as the provider layer itself — future, for teams whose stack already includes a configured `DbContext` |

Conventions that follow from this:

- **DI extension**: `Add{ProviderName}Persistence()` — e.g., `AddSqlitePersistence()`, `AddSqlServerPersistence()`
- **Options class**: `{ProviderName}PersistenceOptions` — e.g., `SqlitePersistenceOptions`, `SqlServerPersistenceOptions`
- **Namespace root**: mirrors project name — e.g., `Aster.Persistence.Sqlite`

### Alternatives considered

| Alternative | Rejected because |
|-------------|-----------------|
| `Aster.Sqlite`, `Aster.SqlServer` | Too shallow — reads as a peer of `Aster.Core` rather than a pluggable infrastructure concern. Harder to filter/glob in tooling and CI. |
| `Aster.Storage.[ProviderName]` | "Storage" is ambiguous in this domain (could mean version history, blobs, or persistence layer). "Persistence" is precise. |
| Everything under `src/core/` | Conflates SDK contracts with provider implementations. `src/persistence/` cleanly separates infrastructure from core abstractions. |

### Consequences

- Future provider projects (`Aster.Persistence.SqlServer`, `Aster.Persistence.EFCore`, etc.) have a predictable home (`src/persistence/`) and a predictable NuGet identity.
- `Aster.Persistence.EFCore` is named for the *abstraction layer* (EF Core), not the underlying database. This is intentional — it accepts a host-configured `DbContext` and the database choice is orthogonal to the provider.
- `src/persistence/` must be added to the solution folder structure if it does not already exist.

---

## Decision 2: Data Access Strategy

### Decision

Persistence providers use the **lowest-dependency, purpose-fit data access layer** for their target backend. For `Aster.Persistence.Sqlite`, this is **raw ADO.NET** (`Microsoft.Data.Sqlite`) with no ORM or micro-ORM.

### Rationale

The Aster persistence schema is deliberately thin: three tables where the payload is an opaque JSON string managed by the provider. The "hard work" — translating the `ResourceQuery` AST to parameterised SQL — is custom regardless of data access layer, because any ORM query-building abstraction would need to be bypassed or heavily guided to support the AST contract without leaking `IQueryable` (a Constitution Principle IV violation).

Given this, the value of adding an ORM dependency is limited:

| Option | Assessment |
|--------|------------|
| **Raw ADO.NET** ✅ | Zero extra dependencies. Full control over connection and transaction lifecycle. Result-set mapping is trivial for three tables. No risk of ORM types or query expressions crossing abstraction boundaries. |
| **Dapper** ⚠️ | Saves result-set → object mapping boilerplate. That boilerplate is small here. Adds a dependency and two `NuGet` entries for a marginal convenience. Valid to reconsider if a future provider grows significantly more complex result shapes. |
| **EF Core (internal)** ❌ | Brings migrations (conflicts with the single-fixed-schema constraint for Phase 2), change tracking (not useful for append-only records), and LINQ expression trees (direct Constitution Principle IV violation risk). The correct role for EF Core is as the *provider itself* — `Aster.Persistence.EFCore` — not as the internal implementation of another provider. |

### Rule for future providers

Apply the same reasoning: use the lowest-dependency, purpose-fit layer.

- `Aster.Persistence.SqlServer`: raw `Microsoft.Data.SqlClient`.
- `Aster.Persistence.EFCore`: accepts an externally-configured `DbContext`; all data access goes through EF Core's own abstractions (no raw ADO.NET therein).
- Dapper may be introduced inside any provider where result-set mapping complexity justifies it, as a per-provider decision.

### Consequences

- `Aster.Persistence.Sqlite` takes a direct `Microsoft.Data.Sqlite` package reference only.
- No shared data access helper library is introduced; any shared utilities (e.g., `JsonSerializerOptions`) are internal to each provider project.
- This decision is revisited if a future provider's result-set complexity makes Dapper materially worthwhile.

---

## Related

- `specs/002-roadmap-next-phase/plan.md` — §Provider Naming Convention, §Data Access Strategy
- `specs/002-roadmap-next-phase/spec.md` — FR-013, FR-014
- `.specify/memory/constitution.md` — Principle IV (Typed & Queryable), Principle V (Provider Agnostic)
