# Roadmap

Aster is delivered in six phases. Each phase builds on the last, with clean extension points so earlier work is not thrown away.

> **Current status:** Phase 1 (Core SDK & In-Memory Engine) — active development.

---

## Phase Overview

| Phase | Title | Status |
|---|---|---|
| **1** | Core SDK & In-Memory Engine | In Progress |
| **2** | Persistence & Querying | Planned |
| **3** | Advanced Indexing & Typed Querying | Planned |
| **4** | Portability & Integration Hooks | Planned |
| **5** | Multi-tenancy, Policies, Advanced Versioning | Planned |
| **6** | Operational Hardening | Planned |

---

## Phase 1 — Core SDK & In-Memory Engine

**Goal:** A fully working in-memory SDK that validates the developer experience, domain model, and query contract before any persistence layer is built.

### Epics

| Epic | Description | DoD |
|---|---|---|
| **1.1** | Core domain types (`ResourceDefinition`, `AspectDefinition`, `FacetDefinition`, `Resource`, `AspectInstance`, `FacetValue`, `ActivationState`) | JSON-serializable, validation rules, clear definition vs instance separation |
| **1.2** | Definition Registry and Builder APIs (`ResourceDefinitionBuilder`, `IResourceDefinitionStore`) | Code-first definition; named + unnamed aspect attachments; definition versioning |
| **1.3** | In-Memory Resource + Versioning/State | Immutable versions; channel-based activation; optimistic concurrency; singleton enforcement |
| **1.4** | Workbench Application (`Aster.Web`) | Read-only JSON endpoints at `/api/definitions` and `/api/resources/{definitionId}` |
| **1.5** | Typed Aspects Foundation | POCO round-trip; `GetAspect<T>` / `SetAspect<T>` / `GetFacet<T>` / `SetFacet<T>` |
| **1.6** | Query Model Contracts | `ResourceQuery` AST; in-memory LINQ evaluator; `Equals` and `Contains` operators |

---

## Phase 2 — Persistence & Querying

**Goal:** Prove the domain model against a real database. One reference backend is built and shipped.

### Epics

| Epic | Description |
|---|---|
| **2.1** | Persistence abstractions (`IResourceWriteStore`, `IResourceReadStore`, optional `IUnitOfWork`) |
| **2.2** | Reference backend — choose one: SQLite+JSON, PostgreSQL+JSONB, or MongoDB |
| **2.3** | Query surface implementation — translate `ResourceQuery` AST to provider queries; paging/sorting |
| **2.4** | Provider migrations / provisioning — `IInfrastructureStep`, auto-run at startup or manual CLI execution |

### Backend candidates

- **SQLite + JSON** — simple, great for local dev and small deployments.
- **PostgreSQL + JSONB** — production-grade, native JSON indexing, recommended for most teams.
- **MongoDB** — document-native, schema provisioning via indexes.

---

## Phase 3 — Advanced Indexing & Typed Querying

**Goal:** Provider capability negotiation, advanced text handling, typed query helpers, and versioned definition schemas.

### Epics

| Epic | Description |
|---|---|
| **3.1** | Advanced indexing logic (`IQueryCapabilities`; portable index field types: `Keyword`, `Text`, `NormalizedText`, `Boolean`, `Integer`, `Decimal`, `DateTime`, `Guid`, `KeywordArray`) |
| **3.2** | Typed aspect querying — `WhereAspect<TAspect>(...)` compiles to `ResourceQuery` predicates via mapping metadata |
| **3.3** | Versioned definition schemas — `ResourceDefinitionVersion` model; resources reference a definition version; upgrade API |

### Planned portable operator set

| Operator | Description |
|---|---|
| `Exists(field)` | Field is present and non-null |
| `Equals(field, value)` | Exact match |
| `NotEquals(field, value)` | Inverse exact match |
| `In(field, values[])` | Any of the values |
| `Range(field, min?, max?)` | Numeric / date bounds |
| `Contains(field, value)` | Substring / token match |
| `StartsWith(field, value)` | Prefix match |
| `ArrayContains(field, element)` | Array includes element |
| `ArrayContainsAny(field, elements[])` | Array includes any element |
| `ArrayContainsAll(field, elements[])` | Array includes all elements |

---

## Phase 4 — Portability & Integration Hooks

**Goal:** Export/import of definitions and resources; lifecycle events for hosts to attach behavior.

### Epics

| Epic | Description |
|---|---|
| **4.1** | Portability primitives — `IPortabilityService`; export/import; deterministic ID remapping |
| **4.2** | Optional: Recipes framework (`Aster.Recipes`) — `IRecipeStep`, `IRecipeExecutor`, built-in steps |
| **4.3** | Host hooks — `OnSaving`, `OnSaved`, `OnActivating`, `OnActivated`, `OnDeactivating`, `OnDeactivated` |

`Aster.Recipes` is an **optional add-on** — Aster Core will not depend on it.

---

## Phase 5 — Multi-tenancy, Policies, Advanced Versioning

### Epics

| Epic | Description |
|---|---|
| **5.1** | Tenant-aware definition scoping; optional shared definitions; tenant-aware query boundaries |
| **5.2** | Retention / archival policies; soft-delete policy aspect; version pruning strategies |

---

## Phase 6 — Operational Hardening

**Goal:** Production-readiness: concurrency safety, benchmarks, migration test harness.

### Epics

| Epic | Description |
|---|---|
| **6.1** | Concurrency and conflicts — optimistic concurrency checks; conflict error model; optional merge strategy hooks |
| **6.2** | Perf and testing harness — benchmark suite; large data test suite; query latency targets |
| **6.3** | Migration hardening — upgrade path tests; idempotency tests; rollback tests; locking strategy |

---

## Planned Package Layout

```
Aster.Abstractions
Aster.Definitions
Aster.Runtime
Aster.Querying
Aster.Indexing
Aster.Persistence.<Backend>   (e.g., SqliteJson, PostgresJsonb, Mongo)
Aster.Hosting
Aster.Recipes                 (optional)
```

---

## Key ADR Candidates

Architectural decisions that should be documented before the relevant phase begins:

1. **Active vs Published semantics** — configurable multi-active with channels (resolved in Phase 1 spec).
2. **Query Model vs IQueryable** — custom AST (resolved in Phase 1 spec).
3. **Reference backend selection** — which database to target first in Phase 2.
4. **Indexing approach** — single built-in engine vs pluggable interface.
5. **Document shape and growth strategy** — snapshots now, deltas + compaction later.
6. **Typed aspect mapping** — how POCOs map to document payload and index fields.
7. **Versioned schemas** — how resources upgrade to newer definition versions.
8. **Text vs NormalizedText semantics** — portable substring matching vs provider full-text.

---

## Related

- [Architecture Overview](Architecture-Overview) — layering and key design decisions
- [Concepts & Terminology](Concepts-and-Terminology) — domain model
- Full roadmap source: [`docs/roadmap.md`](../docs/roadmap.md)
