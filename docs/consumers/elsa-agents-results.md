# Results: Elsa agent definitions on Aster

- **Date:** 2026-10-21
- **Program task:** [#118](https://github.com/valence-works/aster/issues/118) (epic [#69](https://github.com/valence-works/aster/issues/69))
- **Design:** [`elsa-module-design.md`](elsa-module-design.md) (#114)
- **Package:** [`src/integrations/Aster.Elsa.Agents`](../../src/integrations/Aster.Elsa.Agents), tests in [`test/Aster.Elsa.Agents.Tests`](../../test/Aster.Elsa.Agents.Tests)
- **Elsa baseline:** 3.9.0. The Elsa source figures below are from the `elsa-extensions` commit the 3.9.0 packages were built from (`89d4eb9b`).

## Summary

Elsa's agent definitions run on Aster in an unmodified Elsa 3.9 host. Opting in is one call, `UseAgentPersistence(agents => agents.UseAsterAgentStore())`, which passes the Aster store to Elsa's own `UseAgentStore` seam. Leaving it out keeps Elsa's store.

- **What runs on it.** Elsa's agent manager, its kernel configuration and its agent activities all read and write through the Aster store. Tests check this in an in-process Elsa host.
- **What Elsa gains.** Every save is a version, Elsa reads the version active in a channel, and promotion, rollback, history, export, tenant isolation, queryable facets and reverse references come from Aster.
- **Migration.** A one-time, idempotent import moves agents from Elsa's in-memory or EF Core store. It is tested against a live EF Core SQLite database, which Aster then shares.

| Acceptance criterion | Where it is met |
|---|---|
| The entity runs on Aster in an Elsa build behind a flag | `UseAsterAgentStore` on Elsa's `UseAgentStore` seam. `ElsaHostTests` checks that the store is used only when selected, that Elsa's agent manager and kernel configuration run on it, and that Elsa's agent activities follow the version active in the channel. |
| Migration is tested | `AgentImportTests`: from Elsa's in-memory store and from Elsa's EF Core SQLite store, including idempotency, reference ordering and cycles, failures, tenants, and no auto-activation. |
| A results write-up | This document. |

## Lines of code

Elsa's own agent storage is an in-memory store plus an EF Core store with one package per database. The Aster store replaces the EF Core store. Elsa is not changed: nothing is removed from Elsa, and a host that opts in no longer needs Elsa's EF Core agent packages.

| | Files | Lines | Code lines (no blanks or comments) |
|---|---|---|---|
| **Elsa 3.9, EF Core agent store** (`Elsa.Agents.Persistence.EFCore`: store, `DbContext`, configuration, feature) | 5 | 152 | 107 |
| **Elsa 3.9, EF Core provider packages** (SQL Server, PostgreSQL, SQLite, MySQL: context factories, provider extensions) | 8 | 146 | 98 |
| **Elsa 3.9, EF Core migrations** (generated, two per provider plus snapshots) | 20 | 1,823 | 1,465 |
| *Elsa 3.9, in-memory store (`MemoryAgentStore`, kept as Elsa's default)* | *1* | *53* | *46* |
| **Aster.Elsa.Agents, store** (store, aspect mapping, definition and its registration, options, registration extensions) | 10 | 687 | 439 |
| **Aster.Elsa.Agents, migration** (importer, result types) | 5 | 197 | 128 |
| **Aster.Elsa.Agents, tests** (all providers, in-process Elsa host, migration, latency) | 8 | 1,168 | — |

- **Lines added:** 884 lines of library code (567 code lines) in one package, plus tests.
- **Lines a host no longer needs:** 298 hand-written lines (205 code lines) and 1,823 lines of generated migrations, across five Elsa packages.
- **Lines removed from Elsa:** none, by design. The store uses Elsa's existing seam.
- **Why the store is larger than Elsa's EF Core store:**
  - Elsa's EF Core store is a thin wrapper over Elsa's generic `EntityStore`.
  - The Aster store adds work Elsa's store doesn't do: channel reads, soft deletes, name-to-ID reference translation, per-tenant definition registration, and XML documentation on every member.
- **What the Aster package doesn't need:**
  - **No per-provider code or migrations.** The same package runs on Aster's SQLite, PostgreSQL and SQL Server providers, which own their schema.
  - **No MySQL.** MySQL has no Aster provider (see the module design), so MySQL hosts keep Elsa's store or run Aster on a separate database.

## Query latency

**Method.** `AgentStoreLatencyBenchmark` in the test project:

- **How to run it:** `ASTER_AGENTS_BENCHMARK=1 dotnet test test/Aster.Elsa.Agents.Tests -c Release -f net10.0 --filter AgentStoreLatencyBenchmark --logger "console;verbosity=detailed"`.
- **Pairings:** each pairs two stores on the same storage tier:
  - Elsa's `MemoryAgentStore` against the Aster store on Aster's in-memory provider;
  - Elsa's `EFCoreAgentStore` on SQLite against the Aster store on Aster's SQLite JSON provider.
  
  Each store has its own database file, with connection pooling on.
- **Data:** each store holds the same 100 agents, with every field set.
- **Timing:** every operation goes through Elsa's `IAgentStore`, resolved from one DI scope, one call at a time, timed with `Stopwatch` after 50 warm-up calls. The figures are over 500 calls.
- **Machine:** a 4-vCPU Linux container, .NET 10, Release build. The benchmark ran three times. The table shows the median of each run as a range, and one run's p95. Shared machines are noisy, so treat the figures as indicative.

| Operation | Elsa EF Core, SQLite | Aster store, SQLite | Elsa in-memory | Aster store, in-memory |
|---|---|---|---|---|
| `GetAsync` by ID | 641–680 µs (p95 0.92 ms) | 319–502 µs (p95 0.65 ms) | 3 µs | 304–332 µs |
| `FindAsync` by name | 604–696 µs (p95 0.97 ms) | 1.10–2.04 ms (p95 1.43 ms) | 1.45–1.49 ms | 266–583 µs |
| `ListAsync`, 100 agents | 2.11–2.54 ms (p95 4.2 ms) | 2.33–2.44 ms (p95 4.0 ms) | 2 µs | 1.60–1.76 ms |
| `UpdateAsync` (new version, activated) | 2.02–2.40 ms (p95 3.7 ms) | 1.43–1.61 ms (p95 3.4 ms) | 1 µs | 147–181 µs |
| `AddAsync` (version 1, activated) | 1.07–1.19 ms (p95 2.4 ms) | 1.10–1.41 ms (p95 2.0 ms) | 1 µs | 91–107 µs |

- **On a database, the stores are close.** Get by ID and update are faster on Aster. List and add are about even.
- **Find by name is slower on Aster, by about 2×.** The name is a facet inside JSON and Aster compares text case-insensitively, so the query reads more rows, and the store then checks names ordinally. Elsa's EF Core table has an index on `Name`. Elsa's agent manager calls `FindAsync` for its name checks (`IsNameUniqueAsync`, `GenerateUniqueNameAsync`), so this cost falls on editing agents, not on running them.
- **In memory, the gap is large.** Elsa's in-memory store is a dictionary, while Aster's in-memory provider evaluates a portable query over versions, activations and lifecycle markers. The Aster in-memory provider is for tests and development, not for production agent catalogs.
- **Agent references cost one extra bounded read** when a referenced agent is not in the result already. The benchmark agents have no references.
- **Where Elsa reads agents:** the kernel configuration (`StoreKernelConfigProvider`) lists every agent when an agent runs or the activity registry refreshes. At about 2.4 ms for 100 agents on SQLite, that is the same as Elsa's EF Core store.

## New capabilities

Each of these comes from Aster with no change to Elsa, and has a test.

| Capability | How | Test |
|---|---|---|
| **Version history** | Every add or update through Elsa saves an immutable version; `IResourceManager.GetVersionsAsync` lists them. | `Every_update_saves_a_new_version_and_reads_return_the_latest` |
| **Channels** | Elsa reads the version active in `Channel`; two hosts on one database can read `staging` and `production`. | `Stores_on_different_channels_see_the_version_active_in_their_own` |
| **Promotion and review** | With `AutoActivate = false`, saves are drafts; activating a version promotes it. | `Without_auto_activation_saves_are_drafts_until_promoted` |
| **Rollback** | Activating an older version; Elsa's kernel configuration and agent activities follow. | `Activating_an_older_version_rolls_back`, `Elsas_agent_activities_follow_the_version_active_in_the_channel` |
| **A/B variants** | Several active versions in a channel; the store reads the highest. | `With_several_active_versions_the_highest_is_read` |
| **Soft delete and restore** | Deletes keep history; Aster's restore service brings an agent back. | `Deleting_soft_deletes_and_keeps_the_history` |
| **Export** | Portable snapshots of agents with every version, their activations and the agents they reference. | `Agents_export_with_their_history_and_activations` |
| **References** | `AgentConfig.Agents` is a `reference[]` facet: references follow renames, are validated on save, and have a reverse lookup ("which agents use this agent?"). | `Agent_references_are_stored_as_resource_ids_and_read_as_names`, `Reverse_reference_lookup_finds_the_agents_that_use_an_agent` |
| **Queryable facets** | Name, description, function name, prompt, skills and references are facets in Aster's portable query language. | `Facets_make_agents_queryable_through_Aster` |
| **Tenant isolation** | Every call runs in the Aster tenant of Elsa's current tenant, as Elsa's EF Core store filters by tenant. Elsa's in-memory store keeps no tenant at all. | `Each_Elsa_tenant_sees_only_its_own_agents` |
| **One store, every database** | The same package on SQLite, PostgreSQL and SQL Server, with no migrations. | The store, channel and tenant tests run on all four Aster providers. |
| **Workflow and HTTP access** | `Aster.Elsa` activities and `Aster.AspNetCore` endpoints work on agent resources like any other. | (existing `Aster.Elsa` and `Aster.AspNetCore` tests) |

## Behaviour differences from Elsa's stores

These are deliberate. The package README documents each one.

- **Delete is a soft delete.** The agent disappears from Elsa but keeps its versions. Re-adding the same ID throws `DuplicateResourceIdException`; restore it instead.
- **References to unknown agents are rejected** under Aster's default strict reference validation. Elsa's stores keep any string. Elsa 3.9 never reads `AgentConfig.Agents` at run time, so no running agent depends on a dangling name.
- **An agent needs an `AgentConfig`.** Elsa dereferences it when building activities, so the store refuses a null one rather than storing an agent Elsa can't load.
- **Name filters compare ordinally**, as Elsa's in-memory store does. Elsa's EF Core store compares with the database's collation, so on SQL Server it is case-insensitive.
- **Drafts are invisible.** With `AutoActivate = false`, an added agent doesn't exist for Elsa until it is activated, so Elsa's name check doesn't see a draft's name either.

## Observations

- **Elsa 3.9 has no shell-feature seam for agent persistence.** `AgentsPersistenceShellFeature.cs` is empty in the 3.9.0 source. The store plugs into the classic feature system only, unlike `Aster.Elsa`, which ships both.
- **Enabling Elsa's EF Core store disables the Aster store.** `EFCoreAgentPersistenceFeature.Configure()` calls `UseAgentStore` while Elsa applies its features, after the host's own configuration. A host can't select both. The migration host keeps Elsa's store and registers Aster beside it with `AddAsterElsaAgents()`.
- **Elsa's activity registry keeps stale agent activities when no agents are left.** `ActivityRegistry.RefreshDescriptorsAsync` only replaces a provider's descriptors when the provider returns at least one. This is independent of the store.
