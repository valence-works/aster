# Roadmap

Aster is delivered in six phases. Each phase builds on the last, with clean extension points so earlier work is not thrown away.

> **Current status:** Phase 5 in progress — Core SDK, in-memory engine, SQLite JSON persistence/querying, query capability discovery, preflight validation, typed query authoring helpers, provider authoring/conformance support, SQLite facet sorting, portable operators, SQLite date-like ranges, explicit index projection contracts, explicit definition schema upgrade behavior, portability primitives, host lifecycle hooks, explicit tenant scoping, policy foundations, and host-controlled policy application orchestration are available.
>
> For the current implementation trail and next Spec Kit slices, see [`docs/ExecutionRoadmap.md`](../docs/ExecutionRoadmap.md).

---

## Phase Overview

| Phase | Title | Status |
|---|---|---|
| **1** | Core SDK & In-Memory Engine | Complete |
| **2A** | SQLite JSON Persistence & Querying | Complete |
| **3** | Query Capabilities & Typed Querying | Complete |
| **4** | Portability & Integration Hooks | Core Complete |
| **5** | Multi-tenancy, Policies, Advanced Versioning | In Progress |
| **6** | Operational Hardening | Planned |

## Immediate Next Slices

| Slice | Status | Purpose |
|---|---|---|
| **008 Typed Query Authoring Ergonomics** | Landed | Add typed sort helpers and small composition conveniences over the existing `ResourceQuery` AST. |
| **009 Portable Operator Expansion** | Landed | Add operators such as `NotEquals`, `In`, `StartsWith`, and facet value presence with provider capabilities and conformance coverage. |
| **010 SQLite Date-Like Facet Ranges** | Landed | Close the remaining SQLite date-like range capability gap with explicit serialization rules. |
| **011 Explicit Indexing Model** | Landed | Introduce intentional index projection contracts without a hidden planner or runtime scanning. |
| **012 Definition Schema Versions & Upgrade Flow** | Landed | Make long-lived resource schema versioning and upgrade behavior explicit. |
| **013 Portability Primitives** | Landed | Add deterministic export/import primitives for definitions and resources. |
| **014 Host Lifecycle Hooks** | Landed | Add explicit hooks around save, activation, deactivation, and import/export without hidden runtime scanning. |
| **015 Tenant Scoping** | Landed | Add explicit tenant boundaries for definitions, resources, activation, queries, portability, and lifecycle hooks. |
| **016 Policy Foundations** | Landed | Introduce explicit retention, archival, soft-delete, and pruning policy contracts without hidden background processing. |
| **017 Policy Application Orchestration** | Landed | Apply selected archive and soft-delete policy preview outcomes through host-controlled workflows. |
| **018 Lifecycle Restore Workflows** | Next | Preview and explicitly restore archive and soft-delete lifecycle markers without hidden execution or resource version rewrites. |

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
| **2.1** | Persistence abstractions (`IResourceVersionWriter`, `IResourceVersionReader`) |
| **2.2** | Reference backend — SQLite JSON |
| **2.3** | Query surface implementation — translate `ResourceQuery` AST to provider queries; paging/sorting |
| **2.4** | Provider migrations / provisioning — `IInfrastructureStep`, auto-run at startup or manual CLI execution |

Completed implementation note: `Aster.Persistence.SqliteJson` provides the SQLite JSON definition store, resource version reader/writer, activation state persistence, and provider-backed `IResourceQueryService`. Core includes a provider-backed `DefaultResourceManager` that orchestrates lifecycle operations against those primitives.

### Backend notes

- **SQLite + JSON** — selected reference backend; simple, great for local dev and small deployments.
- **PostgreSQL + JSONB** — production-grade, native JSON indexing, recommended for most teams.
- **MongoDB** — document-native, schema provisioning via indexes.

---

## Phase 3 — Query Capabilities & Typed Querying

**Goal:** Provider capability discovery, query preflight validation, typed query helpers, and the foundation for advanced indexing.

### Epics

| Epic | Description |
|---|---|
| **3.1** | Query capabilities (`IResourceQueryCapabilitiesProvider`) and provider preflight validation |
| **3.2** | Typed aspect querying — `TypedQuery.For<TAspect>()` builds portable `ResourceQuery` predicates via convention and per-query overrides |
| **3.3** | Advanced indexing logic; portable index field types such as `Keyword`, `Text`, `NormalizedText`, `Boolean`, `Integer`, `Decimal`, `DateTime`, `Guid`, `KeywordArray` |
| **3.4** | Versioned definition schemas — `ResourceDefinitionVersion` model; resources reference a definition version; upgrade API |

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
3. **Reference backend selection** — SQLite JSON selected for Phase 2A.
4. **Indexing approach** — single built-in engine vs pluggable interface.
5. **Document shape and growth strategy** — snapshots now, deltas + compaction later.
6. **Typed aspect mapping** — how POCOs map to document payload and index fields.
7. **Versioned schemas** — how resources upgrade to newer definition versions.
8. **Text vs NormalizedText semantics** — portable substring matching vs provider full-text.

---

## Related

- [Architecture Overview](Architecture-Overview) — layering and key design decisions
- [Concepts & Terminology](Concepts-and-Terminology) — domain model
- Full roadmap source: [`docs/Roadmap.md`](../docs/Roadmap.md)
