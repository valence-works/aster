# ADR 0001: Control-plane boundary

- **Status:** Accepted
- **Date:** 2026-10-20
- **Program task:** [#79](https://github.com/valence-works/aster/issues/79) (epic [#61](https://github.com/valence-works/aster/issues/61))
- **Evidence:** Elsa consumer spike (#77, `docs/consumers/elsa-spike.md`)

## Context

Aster stores resources as immutable, versioned JSON snapshots whose shape is defined at runtime by resource definitions. That gives every resource type the same versioning, activation channels, tenancy, portability, and definition-driven UIs. It also costs things a hand-modelled relational entity does not pay:

- **Write amplification:** every save appends a full snapshot. Delta storage is deferred (#105).
- **Query cost:** facet filters run against JSON payloads. SQLite has no facet indexes; physical indexes arrive with the Postgres provider (#100).
- **Weaker typing:** dictionary aspects have no compile-time schema, and renaming a facet is a definition upgrade, not a refactor.
- **No joins:** references are resolved by lookup (#86–#88), not relational joins.

Without an explicit boundary, adopters will put high-throughput runtime data into Aster and hit these costs where they hurt most.

## Decision

Aster is a **control-plane store**. It holds data that people or tools **author**, that changes **occasionally**, and that benefits from **history, promotion and portability**. It is not a store for data a system **produces** while running.

### In scope

| Kind | Examples |
|---|---|
| Definitions and configuration | workflow definitions, agent definitions, connector and webhook definitions, feature flags, app settings |
| Authored content and templates | prompts, email and notification templates, CMS-style pages |
| User-defined entities with modest volume | low-code entities in a small business tool, reference and master data |
| Metadata about external things | secret metadata (never values), labels, ownership, deployment targets |

### Out of scope

| Kind | Examples | Use instead |
|---|---|---|
| Runtime execution state | workflow instances, bookmarks, trigger indexes, execution logs, activity journals | the host's own relational or document store |
| High-frequency writes | telemetry, audit streams, counters, queues | append-only stores, time-series databases, queues |
| Secret values | API keys, passwords | a secret store (Key Vault, Secrets Manager); Aster holds metadata only |
| Large binaries | files, images, video | blob storage; Aster stores references |

**Rejected: storing these categories in Aster as well.** Each one conflicts with a property Aster is built on:
- **Runtime execution state and high-frequency writes** change many times per second per record. Every Aster save appends a full immutable snapshot, so these workloads would multiply storage and write cost while gaining nothing from history, channels or portability, which they never use.
- **Secret values** must not travel. Aster exports, imports, versions and keeps every historical snapshot, which is the opposite of what a secret store guarantees: rotation, revocation, access auditing and encryption at rest under a managed key.
- **Large binaries** inflate every snapshot and every export, and JSON payloads are the wrong container for them. A reference to blob storage keeps resources small and portable.

### Heuristics

A resource type belongs in Aster when most of these hold:

1. **Authored, not produced.** A person or a deployment pipeline creates it, not a running process.
2. **Low write rate per resource.** Minutes to months between versions, not milliseconds.
3. **History or promotion matters.** Someone will ask "what changed", "roll it back", or "promote it to production".
4. **Variety over volume.** Many types with modest counts, rather than one type with millions of rows and complex queries.
5. **It travels.** It is exported, imported, or copied between environments or tenants.

Indicative volumes will be recorded once benchmarks exist (#103, #104). Until then, plan for **tens of thousands of resources per definition on SQLite**, and treat an order of magnitude more as needing the Postgres provider with declared index projections.

Latency targets are also deferred to the performance epic (#66). The interim rule: **keep Aster off per-execution hot paths.** Read control-plane data when it changes (through lifecycle hooks) or at startup, and cache the resolved version in the host. If a host would need an Aster query on every request or workflow step to meet its latency budget, that data is runtime data, or the host needs a cache in front of Aster.

### Combining Aster with a host's own model

Hosts keep runtime data in their own store and point at Aster resources by `ResourceId`. A runtime record that depends on a specific snapshot must also persist that snapshot's exact `Version` (or the version-specific `Id`) **before execution starts**. A channel is only a way to *select* a version: one channel can hold several active versions, and it moves when someone promotes or rolls back, so it cannot identify what a past execution used. For Elsa, the `production` channel is single-active: the module promotes with `ActivateAsync(..., allowMultipleActive: false)`, the default, which replaces the previously active version. When a workflow instance starts, the host resolves the one active version, persists the definition's `ResourceId` and that exact version on the instance row, and runs it. If the channel holds no active version, or several (because someone activated with `allowMultipleActive: true`), the start is rejected rather than guessed. The definition itself lives in Aster. Lifecycle hooks (`IResourceLifecycleHook`) let the host react to control-plane changes without polling.

## Consequences

- Product work optimises for in-scope workloads: schema expressiveness (#62–#64), references, portability, and channels. Throughput beyond the documented limits is not a goal.
- The Elsa integration (#69) moves control-plane entities only; runtime entities stay in Elsa's stores. The spike surfaced no pressure to put runtime data into Aster.
- `Aster.AspNetCore` (#108) and the samples (#119, #120) restate this boundary where they introduce Aster.
