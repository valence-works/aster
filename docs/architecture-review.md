# Architecture Review: Aster Roadmap

**Date:** March 4, 2026
**Reviewer:** GitHub Copilot (Software Architect)
**Reference Document:** `docs/roadmap.md`

## Executive Summary

The Aster roadmap outlines a sophisticated, well-thought-out system for managing versioned, composable resources. The influence of systems like Orchard Core is evident but refined into a headless, SDK-first library.

The core strength lies in its **immutable versioning strategy** and the **Activation Channel** concept, which decouples "publishing" from "state". However, the roadmap presents significant risks in the ordering of phases—specifically, deferring the **Query Model** and **Typed Aspects** to later phases. These are foundational architectural drivers that will likely force refactors of the storage layer if not addressed earlier.

## 1. Strengths & Commendations

### 1.1. The Activation Model
The shift from a binary "Draft/Published" state to a **Channel-based Activation model** is elegant.
*   **Why it works:** It natively supports complex scenarios like "Preview", "Staging", "A/B Testing", or "Mobile-Specific" versions without changing the core schema.
*   **Benefit:** decouples the "resource" from the "delivery context".

### 1.2. Immutable Versioning
Committing to **append-only, immutable resource versions** is the correct choice for a system designed for high integrity (Workflows, CMS, Configuration).
*   **Benefit:** Simplifies concurrency (optimistic locking is easier), enables perfect auditing, and allows for "Time Travel" queries naturally.

### 1.3. Definition/Instance Separation
The distinct separation between `ResourceDefinition` (Schema), `Resource` (Identity), and `ResourceVersion` (State) is clean and standardizes the meta-model effectively.

### 1.4. Explicit Non-Goals
Explicitly excluding "UI Rendering" and "Hard Dependencies on specific DBs" keeps the scope manageable and focused on the backend logic.

## 2. Strategic Concerns & Risks

### 2.1. Phasing Risk: Querying is Fundamental (Phase 3 is too late)
**Critique:** The roadmap places "Query & Indexing Model" in Phase 3, after Persistence (Phase 2).
**Risk:** Data storage shapes are dictated by how data needs to be accessed. Validating a persistence model (Phase 2) without a rigorous query model (Phase 3) often leads to a storage rewrite.
**Recommendation:** Pull **Epic 3.1 (Query Surface Area)** into Phase 1 or 2. You don't need the full implementation, but you need the *contract* and *constraints* to ensure the persistence layer can support them (especially for JSONB vs Relational table choices).

### 2.2. Phasing Risk: Typed Aspects (Phase 4 is too late)
**Critique:** "Typed Aspects" (C# POCOs) are pushed to Phase 4.
**Risk:** If the system is built primarily for dynamic "stringly-typed" dictionaries first, adding strong typing later often results in awkward mapping layers. Most developers will want to use Aster with C# classes immediately.
**Recommendation:** Interleave Typed Aspects with Phase 1. The "In-Memory" engine should support C# objects from day one to validate the developer experience (DX).

### 2.3. Complexity of "Named Aspects" and "Facet Definitions"
**Critique:** The hierarchy `Resource -> Aspect -> Facet` is logical. However, allowing **Named Aspects** (multiple attachments of the same Aspect Definition) significantly increases query complexity.
*   *Query:* "Find resources where the 'Owner' aspect named 'Secondary' has value 'Alice'."
*   *Complexity:* This moves joins from `(ResourceId, AspectId)` to `(ResourceId, AspectId, AttachmentName)`.
**Question:** Is this complexity strictly necessary for MVP? Could "Inheritance" of Aspect Definitions solve this? (e.g., `SecondaryOwnerAspect` inherits `OwnerAspect`).

### 2.4. The "Portable Query Logic" Trap
**Critique:** Epics 3.1 implies rebuilding a query engine (Expression Trees -> Portable DSL -> Provider SQL/JSON).
**Risk:** This is historically a "black hole" of effort. Writing a LINQ provider or a custom DSL that works consistently across Postgres JSONB, Mongo, and SQLite is incredibly hard.
**Recommendation:**
*   Avoid over-specifying "Portable Semantics" (like `NormalizedText` vs `Text`) too early unless strictly required.
*   Consider leaning harder on a library that already abstracts this (though non-goals say "No hard dependency on YesSQL", looking at how YesSQL or Marten handles this is wise).
*   **Action:** Define the *subset* of queries strictly. Do not try to support generic `IQueryable`. The "Supported expression forms (MVP)" section is a good guardrail—keep it rigid.

## 3. Modularity & Extensibility

### 3.1. Infrastructure Steps (Changeset Management)
The approach to abstracting migrations (`IInfrastructureStep`) represents a "Least Common Denominator" risk.
*   **Concern:** SQL databases have mature migration tools (FluentMigrator, EF Core). Document DBs have "Provisioning". Trying to wrap both in one `Aster.Persistence` abstraction might limit the power of the specific providers.
*   **Suggestion:** Keep the interface extremely thin. Allow providers to be "smart" rather than forcing the core to orchestrate everything.

### 3.2. Provider Capabilities
The `IQueryCapabilities` negotiation is very smart design. It acknowledges that not all DBs are equal.
*   **Good:** It allows the system to fail fast at runtime if a host configurations a query that the backing store cannot handle.

## 4. Specific "Fixes" for the Roadmap

### Refined Phase 1 (Core & Types)
*   **Add:** `Epic 1.5 - Typed Aspect Foundation`. Don't wait for Phase 4. Even if it's just serialization, let developers define schemas via classes early.
*   **Add:** `Epic 1.6 - Query Contract`. Define the `ResourceQuery` object model now, even if the implementation is just in-memory LINQ.

### Refined Phase 2 (Persistence)
*   **Merge:** `Epic 3.1` (Query Surface) needs to inform `Epic 2.1` (Persistence Abstractions). The "Write Model" is easy; the "Read/Index Model" is hard. You cannot separate them effectively.

### Simplification Opportunity
*   **Facets:** Do we need `FacetDefinition`s as runtime entities?
    *   *Alternative:* If Aspects are typed classes, the "Facets" are just properties. Maybe `AspectDefinition` is sufficient, and `FacetDefinition` is purely implicit/metadata? This would reduce the graph size and management overhead significantly.

## 5. Conclusion

The roadmap is robust and reflects deep domain experience. The primary recommendation is to **pull the "Hard Problems" (Querying and Typing) forward**. Building a storage engine (Phase 2) without proving the query pattern (Phase 3) is the biggest architectural risk in the current plan.

**Verdict:**
*   **Vision:** A
*   **Architecture:** A-
*   **Execution Plan:** B (Rearrange phases to de-risk querying).

---

## 6. Thread-Safety Audit — `Aster.Core` In-Memory Implementations

**Audit Date:** Phase 1 completion  
**Scope:** `InMemoryResourceDefinitionStore`, `InMemoryResourceStore`, `InMemoryResourceManager`, `InMemoryQueryService`

### 6.1. Primitive: `ConcurrentDictionary<K,V>`

All top-level dictionaries use `ConcurrentDictionary<K,V>` (with `StringComparer.Ordinal` or default):

| Structure | Dictionary | Key | Notes |
|---|---|---|---|
| `InMemoryResourceDefinitionStore.definitions` | `ConcurrentDict<string, List<ResourceDefinition>>` | `DefinitionId` | Bucket per definition |
| `InMemoryResourceStore.Versions` | `ConcurrentDict<string, List<Resource>>` | `ResourceId` | Bucket per resource |
| `InMemoryResourceStore.Activations` | `ConcurrentDict<string, ConcurrentDict<string, HashSet<int>>>` | `ResourceId` → `Channel` | Nested CD |

`ConcurrentDictionary.GetOrAdd` is **atomic for bucket creation** — two concurrent threads cannot both insert the same key with different lists. The returned reference is stable and will be the canonical list used by all future operations.

### 6.2. `List<T>` mutations — `lock(list)`

`List<T>` is **not thread-safe**. Every mutation uses `lock(versions)` or `lock(definitions)`:

```
Definition registration:  lock(versions) { versions.Add(versionedDefinition); }
Resource version save:    lock(versions) { versions.Add(resource); }
Resource version read:    lock(versions) { return versions[^1]; }
```

**Assessment: Correct.** The lock is taken on the list object itself (a fine-grained lock per resource/definition), which is safe because the list reference is obtained from `GetOrAdd` and never replaced.

**Read operations** that return snapshots (e.g. `[..versions]`) also hold the lock, preventing torn reads during concurrent appends.

### 6.3. `HashSet<int>` mutations — `lock(channelActivations)`

Activation sets are `HashSet<int>` stored inside the nested `ConcurrentDictionary`.  
A `lock` is taken on the inner `ConcurrentDictionary<string, HashSet<int>>` (`channelActivations`) for all reads / writes of the `HashSet`:

```csharp
lock (channelActivations)
{
    newActiveVersions.Add(version);
    channelActivations[channel] = newActiveVersions;     // replace-on-write
}
```

**Assessment: Correct.** The outer `ConcurrentDictionary` provides bucket isolation per `ResourceId`; the inner lock protects cross-channel consistency within a single resource.

### 6.4. Known Edge Cases

| Scenario | Behaviour | Risk |
|---|---|---|
| **Concurrent definition registration** for the same `DefinitionId` | Both threads call `GetOrAdd` → same list reference returned; `lock` serialises the `Add` | **Safe** |
| **Concurrent `CreateAsync`** with different `ResourceId`s | Independent buckets in `store.Versions` → no contention | **Safe** |
| **Concurrent `CreateAsync`** with the **same caller-supplied `ResourceId`** | `ContainsKey` check + `GetOrAdd` is not atomic; two threads could both pass `ContainsKey == false` and both call `GetOrAdd`, but `GetOrAdd` is atomic so only one list is stored. The second `SaveVersionAsync` would push a duplicate V1 onto the same list under lock. | **Minor risk** — callers supplying explicit `ResourceId`s should guarantee uniqueness externally, or use the generated ID path. A future improvement: replace `ContainsKey + GetOrAdd` with an atomic `TryAdd` guard. |
| **Singleton enforcement race** | `GetResourceIdsForDefinition` iterates `store.Versions.Keys` without a global lock; two concurrent `CreateAsync` calls for the same singleton definition could both see an empty list and both succeed. | **Acceptable for Phase 1** in-memory usage. A future improvement: use a `SemaphoreSlim` per `definitionId` for singleton enforcement. |
| **`GetResourceIdsForDefinition` snapshot** | Iterates the snapshot of `store.Versions.Keys` at call time; concurrent insertions may not be observed. | **Acceptable for Phase 1.** This is a design property of `ConcurrentDictionary` enumeration. |
| **`ListDefinitionsAsync` ordering** | Iterates `definitions.Values` without a global lock; the ordering of entries is non-deterministic under concurrent writes. | **Expected / documented.** Callers should not assume ordering. |

### 6.5. `InMemoryQueryService` — read-only, no locks needed

`QueryAsync` only reads `store.Versions` (immutable snapshots) using `ConcurrentDictionary` enumeration.  
`Resource` is a `sealed record` whose properties are `init`-only — there is no mutation during query evaluation.  
`ValidateFilterExpression` is a pure AST walk with no shared state.

**Assessment: Inherently thread-safe for reads.**

### 6.6. Summary

The Phase 1 in-memory implementation is **safe for all normal concurrent usage patterns** (concurrent readers + sequential or low-concurrency writers per resource). The two known races (same-ID create, singleton enforcement) are acceptable trade-offs for an in-memory prototype and are documented above for future provider implementors to address with stricter guards.

