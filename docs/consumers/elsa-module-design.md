# Elsa module design: packaging, DI and persistence

- **Date:** 2026-10-21
- **Program task:** [#114](https://github.com/valence-works/aster/issues/114) (epic [#69](https://github.com/valence-works/aster/issues/69))
- **Inputs:**
  - the spike findings and go decision ([`elsa-spike.md`](elsa-spike.md), #78)
  - ADR 0001 (control-plane boundary)
  - ADR 0004 (SQL Server strategy, #102)
- **Elsa baseline:** 3.9.0, checked against the published packages. Elsa targets `net8.0`, `net9.0` and `net10.0`, the same as Aster.

## Decisions at a glance

| Question | Decision |
|---|---|
| Where the module lives | This repository, as `Aster.Elsa` (and later `Aster.Elsa.Studio`). It can move to Elsa's extension repositories once Aster reaches 1.0. |
| How it registers | One Elsa feature in both of Elsa's feature systems: a `FeatureBase` (`elsa.UseAster(...)`) and a CShells `IShellFeature`. |
| Tenants | Elsa's `ITenantAccessor.TenantId` maps to `TenantScope`: `""` → `TenantScope.Default`, any other ID → `TenantScope.FromTenantId(id)`. The agnostic `*` and an Elsa tenant literally named `default` are refused. |
| Persistence | The Aster provider follows Elsa's EF Core provider: SQL Server → SQL Server JSON, PostgreSQL → Postgres JSONB, SQLite → SQLite JSON. Aster uses the same database and connection string by default, in its own tables, bootstrapped by Aster rather than by Elsa's EF migrations. |
| First entity | **Agent definitions** (#118), through Elsa's own `IAgentStore` seam, with no change to Elsa. Labels come second. Workflow definitions and secrets don't move. |

## Where the module lives

**This repository, `src/integrations/Aster.Elsa`.**

- **Aster's API is the part that still moves.** Aster is pre-1.0. Facet types, references, query validation and hook semantics changed in the last month (#123, #124, #194, #219). In this repository, the PR that changes an Aster API also updates the module, and CI catches the break in the same run. In Elsa's repository, every Aster change would need a coordinated release first.
- **Elsa already exposes the seams the module needs.** Activities, triggers, `IStimulusSender`, `ITenantAccessor`, and the pluggable stores (`IAgentStore`, `ILabelStore`) are all public in Elsa 3.9. The module doesn't need Elsa internals, so it doesn't need to live next to them.
- **Release cadence trade-off.** The cost is that Elsa minor releases can break the module without Elsa's CI noticing. To mitigate it:
  - the module depends on Elsa `[3.9.0, 4.0.0)`;
  - CI builds and tests against the lower bound;
  - a scheduled job tests against the latest 3.x and opens an issue when it fails.
  
  Aster releases ship the module at Aster's version. Its package description and README state the Elsa range it supports.
- **Exit path.** After Aster 1.0, when the API is stable, the module can move to Elsa's extensions repository if Elsa's maintainers want to own it. Nothing in this design depends on where it lives.

**Rejected: Elsa's repository first.** It suits a stable dependency. With Aster changing weekly, it would turn every Aster change into a cross-repository release.

## Packages

| Package | Contents | Depends on |
|---|---|---|
| `Aster.Elsa` | Feature registration, tenant mapping, activities (#115), lifecycle triggers (#116) | `Aster.Core`, `Elsa.Workflows.Runtime` (which brings `Elsa.Common`) |
| `Aster.Elsa.Agents` (#118) | `IAgentStore` adapter and the one-time import from Elsa's agent store | `Aster.Elsa`, `Elsa.Agents.Persistence` |
| `Aster.Elsa.Labels` (later) | `ILabelStore` and `IWorkflowDefinitionLabelStore` adapters | `Aster.Elsa`, `Elsa.Labels` |
| `Aster.Elsa.Studio` (#117) | Elsa Studio (Blazor) module: definition list, resource editors, version history | `Aster.AspNetCore` endpoints over HTTP; Elsa Studio packages |

`Elsa.Workflows.Runtime` reaches `CShells.AspNetCore.Abstractions` (through `Elsa.Tenants` and `Elsa.Api.Common`), which declares a framework reference to `Microsoft.AspNetCore.App`. So `Aster.Elsa` brings in the ASP.NET Core shared framework transitively, as any Elsa 3.9 host already has. It does not depend on `Aster.AspNetCore`.

NuGet has no optional dependencies, so each store adapter is its own package. A host that doesn't move agents or labels never installs Elsa's agents or labels packages through Aster.

Persistence providers stay in their own packages. The host references the one that matches its Elsa database. `Aster.Elsa` doesn't pull in all three.

## Registration

Elsa 3.9 has two feature systems:
- the classic `FeatureBase` / `IModule` system, used by `AddElsa(elsa => elsa.UseX())`;
- CShells shell features, marked with `[ShellFeature]`, for shell-based hosts.

Elsa's own modules ship both (for example `LabelsFeature` and `AgentPersistenceFeature`), and so does Aster. Both call one internal registration method, so they can't drift.

```csharp
// Classic Elsa host (illustrative; final names are settled in #115).
builder.Services
    .AddAsterCore()
    .AddAsterSqlServerJson(options => options.ConnectionString = connectionString); // the host picks the provider

builder.Services.AddElsa(elsa => elsa
    .UseAster()                                                 // tenant mapping, activities, triggers
    .UseAgentPersistence(agents => agents.UseAgentStore(sp =>   // #118: agents on Aster, opt-in
        sp.GetRequiredService<AsterAgentStore>())));
```

- **Provider registration stays with the host.** `UseAster()` doesn't choose a provider. The host registers Aster's provider with the existing `AddAster*` calls, as any Aster host does. Elsa's persistence features don't expose a connection string the module could reuse safely, and guessing one would hide a configuration choice.
- **Opting in is the feature flag.** A store adapter is active only when the host passes it to Elsa's own seam (`UseAgentStore`, `LabelsFeature.LabelStore`). That satisfies #118's "behind a flag" without a new switch, and leaving it out keeps Elsa's built-in store.
- **Feature dependencies.** The Aster feature depends on Elsa's workflow management feature (activities are registered through `ManagementOptions.ActivityTypes`) and its workflow runtime feature (to run them and, later, for triggers). It works with or without Elsa's multitenancy feature: without it, every call uses the default tenant.

## Tenants

Elsa scopes everything by `Entity.TenantId`, read through `ITenantAccessor.TenantId`. Aster scopes every call by `TenantScope`. The module resolves one from the other on every call, from inside the Elsa execution context, rather than caching a scope.

| Elsa tenant ID | Aster scope | Why |
|---|---|---|
| `""` (`Tenant.DefaultTenantId`) | `TenantScope.Default` (`"default"`) | Both are each product's single-tenant default. |
| any other ID | `TenantScope.FromTenantId(id)` | Exact match, no transformation, so exported snapshots name the same tenant in both products. |
| `"*"` (`Tenant.AgnosticTenantId`) | refused (`TenantScopeException`) | Aster has no cross-tenant scope. An agnostic read or write would silently pick one tenant. |
| `"default"` | refused (`TenantScopeException`) | It would alias Elsa's default tenant onto the same Aster scope and share its data. |
| whitespace only | refused (`TenantScopeException`) | Aster tenant IDs can't be blank, and trimming would alias it onto another tenant. |

The refusals throw, so a misconfigured tenant fails at its first Aster call rather than reading or writing another tenant's data.

Trigger dispatch (#116) goes the other way: the lifecycle hook context carries the Aster tenant, and the module pushes the matching Elsa tenant with `ITenantAccessor.PushContext` before sending the stimulus, so the workflow starts in the right tenant.

## Persistence

### Provider pairing

| Elsa persistence | Aster provider | Status |
|---|---|---|
| EF Core SQL Server | `Aster.Persistence.SqlServerJson` | #152 (ADR 0004) |
| EF Core PostgreSQL | `Aster.Persistence.PostgresJsonb` | shipped (#179, #182, #183, #192) |
| EF Core SQLite | `Aster.Persistence.SqliteJson` | shipped |
| EF Core MySQL or Oracle, MongoDB | none | Not planned. Such a host runs Aster on a separate Postgres or SQLite database. A provider is added only on demand, through the shared relational core (ADR 0004). |

### Same database, separate tables

**By default, Aster uses the same database and connection string as Elsa, in its own tables.**

- **One database to operate.** A single backup and restore unit, and a single connection string to configure.
- **No table collisions.** Aster's tables are `resource_definitions`, `resource_versions`, `activation_states`, `lifecycle_markers`, `aspect_definitions` and `aster_infrastructure_steps`. Elsa's EF Core tables use other names, and on SQL Server and PostgreSQL they live in Elsa's `Elsa` schema (`ElsaDbContextBase.ElsaSchema`).
- **Aster owns its schema.** Aster's tables are created and upgraded by Aster's infrastructure runner (#99), which is idempotent and safe under concurrent startup. They are not part of Elsa's EF migrations, so an Elsa upgrade never touches them. `InitializeSchema = false` with `IInfrastructureRunner` covers hosts that apply schema changes in a deployment step.
- **A separate database stays possible.** It is one connection-string change, and needed only for the unpaired providers above.

**No shared transactions.** Elsa's EF Core writes and Aster writes use different connections. Nothing in the module needs them in one transaction:
- Activities write through Aster only.
- Triggers react after Aster commits (next section).
- The `IAgentStore` adapter writes Aster only.

## Lifecycle events and triggers (direction for #116)

- **Dispatch.** Triggers are fed by an `IResourceLifecycleHook` whose after-hooks call Elsa's `IStimulusSender`. That covers both starting trigger-bound workflows and resuming bookmarks.
  - **Stimulus.** Each trigger activity type (Resource Saved, Activated, Deactivated, Archived) receives a stimulus carrying the values its filters index: definition ID, channel and tenant. Elsa then matches only the workflows whose filters fit. #116 settles the exact payload and adds tests against Elsa 3.9 for both paths.
  - **Rejected: Elsa's bookmark queue.** `IBookmarkQueue` only resumes existing bookmarks. It can't start a workflow from a trigger, so it doesn't replace an outbox.
- **After-hooks must mean "committed".** Today after-hooks also run when the write failed. #219 fixes that, and #116 depends on it.
- **Delivery is at-most-once.** If the process stops between the Aster commit and the stimulus, the event is lost; a failed workflow start is logged and never rolls back the Aster write, as #116 requires. A durable outbox would mean persisting events in Aster, which ADR 0001 keeps out of scope. It becomes a follow-up only if a consumer needs it.
  - **Failure handling.** The `Aster.Elsa` hook catches `SendAsync` failures, logs them with the resource, version, channel and tenant, and doesn't rethrow, so neither the Aster caller nor other hooks see them. Nothing retries automatically: a retry could start a workflow twice. #116 tests a committed write followed by a failing send.
- **Missing hook point: archive.** "Resource Archived" (#116) has no hook point today. Archiving goes through the lifecycle marker service, which has no hooks. #116 adds after-archive and after-restore points to `IResourceLifecycleHook`, which are additive default interface members. They are dispatched after the marker write commits, under the same committed-only rule as #219.

## Activities (direction for #115)

Activities use the public Aster services:
- `IResourceManager` for save, get, activate and deactivate;
- `IResourceQueryService` for queries;
- `IResourceLifecycleMarkerService` for archive.

Both rules below are reused from `Aster.AspNetCore`, so a client sees the same behaviour over HTTP and in a workflow:
- **Stable fault codes.** Aster's diagnostic codes surface as activity faults with the same code, including the concurrency conflict on `BaseVersion`.
- **Archived resources are hidden by default.** Queries hide archived and soft-deleted resources unless the activity opts in (spike decision F12).

Promotion between channels is a composite activity over the two explicit calls (spike decision F7). It faults when the source channel has no active version, or several (A/B), instead of guessing.

**Naming.** Version inputs are named `ResourceVersion`: Elsa's `Activity.Version` is the activity type's own version, so an input called `Version` would hide it.

**Fault codes are shared by convention.** `Aster.AspNetCore` maps exceptions to `IResult` problems, which an activity can't reuse, so `Aster.Elsa` mirrors the same mapping with the same codes. Comments on both mappings say to change them together.

## Which entities move, and in what order

1. **Agent definitions (#118): first.** This confirms the spike's recommendation, and Elsa 3.9 makes it cheaper than the spike assumed.
   - **No change to Elsa.** `AgentPersistenceFeature.UseAgentStore` accepts any `IAgentStore`. An Aster-backed store makes agents versioned without touching Elsa: each update saves a version, and the store reads the version active in a configured channel. History, channel promotion, rollback and export come for free.
   - **References.** `AgentConfig.Agents` and `Skills` are lists of names. They map to `reference[]` facets once #222 lets a typed aspect declare them.
   - **Migration.** A one-time import reads Elsa's existing store and saves through the adapter (#118).
2. **Labels: second.** `LabelsFeature.LabelStore` and `WorkflowDefinitionLabelStore` are the same kind of seam. It is low risk, but low signal: labels gain little from versions or channels, and the spike already proved array facets for tagging. It comes after #118 as a smaller follow-up.
3. **Not moving:**
   - **Secrets.** Elsa's secret stores hold values, which ADR 0001 keeps out of Aster. Elsa doesn't separate secret metadata from values, so there is nothing to move without changing Elsa.
   - **Workflow definitions.** Elsa already versions and publishes them, and its designer depends on a large query surface. Revisit only after #118's results.

## Consequences

- #115, #116 and #118 are built in this repository against Elsa 3.9 packages, with tests that run workflows in process. #117 follows the same layout as `Aster.Elsa.Studio`.
- #116 depends on #219 (committed-only after-hooks) and adds the archive and restore hook points.
- #118 builds `Aster.Elsa.Agents` and depends on #222, so `AgentConfig.Agents` can map to `reference[]` facets.
- SQL Server hosts depend on #152.
- `Aster.Core` and the provider packages gain no dependency on Elsa. Only the `Aster.Elsa*` packages reference Elsa.
