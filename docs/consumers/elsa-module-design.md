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
| `Aster.Elsa.Studio` (#117) | Elsa Studio (Blazor) module: definition list, resource lists and editors, version history with channel activation, reference picker | `Elsa.Studio.UIHints` `[3.9.0, 4.0.0)`; the `Aster.AspNetCore` endpoints over HTTP, with no package reference to Aster |

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
    .UseAgentPersistence(agents => agents.UseAsterAgentStore())); // #118: agents on Aster, opt-in (calls UseAgentStore)
```

- **Provider registration stays with the host.** `UseAster()` doesn't choose a provider. The host registers Aster's provider with the existing `AddAster*` calls, as any Aster host does. Elsa's persistence features don't expose a connection string the module could reuse safely, and guessing one would hide a configuration choice.
- **Opting in is the feature flag.** A store adapter is active only when the host passes it to Elsa's own seam (`UseAgentStore`, `LabelsFeature.LabelStore`). For agents, `UseAsterAgentStore()` makes that call (see [#118 as built](#118-as-built-deviations)). That satisfies #118's "behind a flag" without a new switch, and leaving it out keeps Elsa's built-in store.
- **Feature dependencies.** The Aster feature depends on Elsa's workflow management feature (activities are registered through `ManagementOptions.ActivityTypes`) and its workflow runtime feature (to run them, and for the triggers' `IStimulusSender`). It works with or without Elsa's multitenancy feature: without it, every call uses the default tenant.

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
- **Reverse mapping.** `AsterTenantMapping.ToElsaTenantId` maps `TenantScope.Default` to `""` and any other scope to its ID.
- **Finding the tenant.** The module looks the tenant up with Elsa's `ITenantFinder`, which reads `ITenantsProvider`, without activating tenants. Elsa's default tenant is assumed to exist, as Elsa does when its provider lists none. An Aster tenant with no Elsa tenant sends nothing (logged at debug level): no workflow could run there.

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

## Lifecycle events and triggers (#116)

- **Dispatch.** Triggers are fed by an `IResourceLifecycleHook` whose after-hooks call Elsa's `IStimulusSender`. That covers both starting trigger-bound workflows and resuming bookmarks.
  - **Rejected: Elsa's bookmark queue.** `IBookmarkQueue` only resumes existing bookmarks. It can't start a workflow from a trigger, so it doesn't replace an outbox.
  - **Inline.** `IStimulusSender` runs the workflows it starts or resumes in the calling flow, until they finish or suspend, so the Aster call returns after them. Sending isn't tied to the Aster caller's cancellation token: the change it reports is already committed.
- **Trigger activities.** Resource Saved, Resource Activated, Resource Deactivated and Resource Archived (`Aster.ResourceSaved` and so on). Each has a `DefinitionId` filter; Activated and Deactivated also have `Channel`. The result is a `ResourceLifecycleEvent` (kind, tenant, resource ID, definition ID, version, channel, save kind, operation ID).
  - **Resource Archived reports archiving only.** The archive hook points also cover soft-deletes; the trigger ignores those.
- **Stimulus and matching.** The stimulus is `ResourceLifecycleStimulus(TenantId, DefinitionId, Channel)`, sent with the trigger's activity type name. The trigger's `GetTriggerPayload` and its bookmark (created with `IncludeActivityInstanceId = false`) return the same record built from its own filters, so Elsa's `IStimulusHasher` gives both sides the same hash exactly when every value matches. Tests compare the hashes of an indexed trigger and of a bookmark with the hash of the stimulus.
  - **Empty filters.** An empty filter is `null` in the payload. Elsa matches hashes exactly, so the hook sends one stimulus per combination of the event's values and `null`: two for Saved and Archived, four for Activated and Deactivated. A trigger matches exactly one of them, so each matching workflow fires once per event.
  - **Tenant.** The tenant isn't a designer input. A trigger matches events from the Elsa tenant its workflow runs in: the payload's `TenantId` is the current Elsa tenant when Elsa indexes the trigger or creates the bookmark, and the hook sends in the Elsa tenant of the Aster write. Elsa's tenant-aware trigger and bookmark stores filter the same way.
  - **Elsa's bookmark queue.** For a stimulus that resumes no bookmark, Elsa's `StimulusSender` queues it for `BookmarkQueuePurgeOptions.Ttl` (10 minutes by default). A matching bookmark created in that window resumes on the earlier event. This is Elsa's behaviour for every stimulus.
- **Only committed changes trigger.** After-hooks run even when the write failed, so before and after calls stay paired. Since #219 they carry `OperationStatus` and `OperationFailure`, and the trigger hook sends a stimulus only when `OperationStatus` is `Succeeded`.
- **Delivery is at-most-once.** If the process stops between the Aster commit and the stimulus, the event is lost; a failed workflow start is logged and never rolls back the Aster write, as #116 requires. A durable outbox would mean persisting events in Aster, which ADR 0001 keeps out of scope. It becomes a follow-up only if a consumer needs it.
  - **Failure handling.** The `Aster.Elsa` hook catches `SendAsync` failures, logs each as a warning with the resource, version, channel and tenant, and doesn't rethrow, so neither the Aster caller nor other hooks see them. Nothing retries automatically: a retry could start a workflow twice. Tests cover a committed write followed by a failing send, and a failed write that sends nothing.
- **Archive and restore hook points.** #116 adds `OnBefore/AfterArchiveAsync` and `OnBefore/AfterRestoreAsync` to `IResourceLifecycleHook`, as additive default interface members, with a `ResourceArchiveLifecycleContext` (resource ID, definition ID, marker). They follow the same rule as the other points: paired with a before point, run around the marker write, with the outcome in `OperationStatus`.
  - **Where they run.** Around every marker write and clear: the marker service, the restore service, and policy application, which archives through the same code. Only calls that write run them.
  - **Archive covers soft-delete.** The archive points run for both marker states; `Marker.State` says which.
  - **Rejections are diagnostics.** These services return per-resource results, so a rejection or hook failure becomes a diagnostic on the result (path `lifecycle/<point>`), as the portability service does, rather than a `LifecycleHookException`.

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

## #118 as built: deviations

`Aster.Elsa.Agents` follows this design. Where it had to differ or decide something the design left open, it is recorded here. The [results write-up](elsa-agents-results.md) has the measurements.

1. **Registration goes through an extension, not the store type.** The store is internal. The host calls `agents.UseAsterAgentStore(options => …)` on Elsa's `AgentPersistenceFeature`. That call registers the services and passes the store to `UseAgentStore`, so the opt-in is still Elsa's own seam. `AddAsterElsaAgents()` registers the services without selecting the store, for a migration host.
2. **Classic feature system only.** Elsa 3.9's agent persistence has no CShells shell feature (`AgentsPersistenceShellFeature.cs` is empty in the 3.9.0 source), so there is no seam for a shell feature to plug into. `Aster.Elsa` itself still ships both.
3. **`AgentConfig.Agents` holds resource IDs, not names.** Elsa names agents by `Name`, but a `reference[]` value must be a target `ResourceId`: Aster's strict reference validation rejects any other value. The store translates names to IDs on save, and IDs back to current names on read. As a result, references follow renames and have a reverse lookup. A name that matches no agent is stored as written, which strict validation rejects.
4. **`AgentConfig.Skills` is `string[]`, not `reference[]`.** Elsa registers skills in code (`ISkillsProvider`), not as stored entities. A skill reference would have no target, and strict validation would reject every save that names a skill.
5. **Saves activate by default.** `AsterAgentStoreOptions.AutoActivate` (default `true`) activates every save in the configured channel (`Channel`, default `production`), so Elsa behaves as with its own stores. With `false`, saves are drafts until promoted through Aster.
6. **Delete is a soft delete.** `DeleteAsync` applies a `SoftDeleted` lifecycle marker. Versions and activations stay, reads skip the agent, and Aster's restore service brings it back.
7. **The migration saves through the store, not through portability.** #118's scope mentions portability primitives. As this design says, the import reads Elsa's store and saves through the adapter instead. That way imported agents get the same name-to-ID translation, definition registration and activation as any other save, and a re-run skips agents that already exist.
   - Agents are imported after the agents they reference.
   - Reference cycles are saved in two versions, the first without the references into the cycle.
   - Imported agents are activated whatever `AutoActivate` says.
8. **Elsa's EF Core agent store can't run beside the Aster store in one Elsa module.** `EFCoreAgentPersistenceFeature.Configure()` calls `UseAgentStore` while Elsa applies features, after the host's configuration, so the EF Core store wins. The migration therefore runs in a host that still uses Elsa's store, with `AddAsterElsaAgents()` beside it; afterwards the host switches to `UseAsterAgentStore()`.

## Elsa Studio module (#117)

`Aster.Elsa.Studio` was built against the Elsa Studio 3.9.0 packages. Every Elsa Studio API it uses was checked in the decompiled 3.9.0 assemblies (`net8.0`):

| API | Package | How the module uses it |
|---|---|---|
| `IFeature` / `FeatureBase` | `Elsa.Studio.Core` | `AsterStudioFeature`, registered as a scoped `IFeature`. The shell's `App` adds the assembly of every `IFeature` to its router's `AdditionalAssemblies`, which is how the module's pages become routable. |
| `IMenuProvider`, `MenuItem`, `MenuItemGroups.General` | `Elsa.Studio.Core` | The **Resources** menu entry (`aster/definitions`). |
| `IUIHintHandler`, `IUIHintService` (`DefaultUIHintService`: first handler whose `GetSupportsUIHint` matches), `AddUIHintHandler<T>` | `Elsa.Studio.Core` | Each form field renders through `IUIHintService.GetHandler(hint).DisplayInputEditor(context)`. The reference picker is registered as one more handler. |
| `DisplayInputEditorContext`, `InputDescriptor` (`IsWrapped`, `UIHint`, `UISpecifications`) | `Elsa.Studio.Core`, `Elsa.Api.Client` | One context per facet, as an unwrapped input. |
| `InputUIHints` and the `UISpecifications` readers (`GetSelectList`, `GetCheckList`, `GetRadioList`, `GetCodeEditorOptions`) | `Elsa.Studio.UIHints` | The hint names the mapping produces, and the shape of the options it passes. Tests read the options back with Elsa's own readers. |
| `AddDefaultUIHintHandlers` | `Elsa.Studio.UIHints` | Called by `AddWorkflowsModule` (`Elsa.Studio.Workflows`); the host needs it for the editors. |

Elsa Studio 3.9 ships `net8.0`, `net9.0` and `net10.0`, so the module multi-targets the same three as the rest of Aster. Its UI library is MudBlazor 9.0.0, which `Elsa.Studio.Core` brings; the module doesn't reference MudBlazor itself.

**Decisions:**
- **Elsa Studio's own editors, reused.** Elsa's UI-hint editors are built for activity inputs, but they only need a `DisplayInputEditorContext`. The module describes each facet as an unwrapped input (`IsWrapped = false`). The editor then shows no expression syntax picker and writes a plain value: a string, a `bool`, a list of strings, or a `WrappedInput` literal from the JSON and code editors. The module converts it to the facet's JSON type. The workflow definition, activity and activity descriptor in the context are placeholders; the code editor and field extensions read them.
- **Forms from the JSON Schema export only.** The form is built from `GET /definitions/{id}/schema` (#106) and its `x-aster-*` keywords, at the definition version the resource was saved against. No definition-specific code exists anywhere, so a definition registered at runtime is editable at once.
- **HTTP only.** The module references no Aster package. Its client and DTOs are internal and read the endpoints' JSON loosely (enum names under any naming policy). It mirrors the standard UI hint names, as Aster Studio's SPA does.
- **Diagnostics by request-body path.** Query diagnostics carry `path` (#194). Save diagnostics carry `aspectKey`, `facetId` and `elementIndex`, which the module turns into `aspects.{aspectKey}.{facetId}`. Each lands on the field with the longest matching path, at a member or index boundary; the rest show as an alert.
- **Minimal public surface.** Public: `AddAsterStudioModule`, the three routable pages (the Blazor router only finds exported component types) and `ReferencePicker`, which hosts can reuse. Razor compiles every component as public, so the module has no `_Imports.razor` (it would compile to a public `_Imports` class). Razor emits `BuildRenderTree` without nullable annotations, so RS0041 is off in this project.

**Deviations from the plan above and from `docs/ui-hints.md`:**
- **`switch` renders as `checkbox`.** `docs/ui-hints.md` mapped it to `SwitchEditor`. In Elsa Studio 3.9, `switch-editor` is the Switch activity's case editor, not an on/off toggle.
- **No dedicated editors for `number`, `secret`, `markdown` and `html`.** Elsa Studio 3.9 has no numeric, masked, Markdown or rich-text UI-hint editor. `number` and `secret` use `singleline` (`number` is parsed and validated as a number, and `secret` is not masked), and `markdown` and `html` use `multiline`.
- **The reverse lookup goes through the query endpoint.** `Aster.AspNetCore` has no referrers endpoint. The **Referenced by** tab reads every definition's schema and runs `POST /resources/query` with a `references` filter for each reference facet that may point to the resource. That costs one request per definition plus one per such facet.
- **No sample Studio host.** A runnable host needs an Elsa server, the Studio shell and its login. The package README and the wiki document the wiring instead, and CI gains no job: the tests render the pages with bUnit against `Aster.AspNetCore` on TestServer.
- **Not in the module:** definition and aspect editing (Aster Studio does it), and archive, restore and schema upgrade, which `Aster.AspNetCore` doesn't expose yet.

## Consequences

- #115, #116 and #118 are built in this repository against Elsa 3.9 packages, with tests that run workflows in process. #117 follows the same layout as `Aster.Elsa.Studio`, with tests that render its pages with bUnit (see [Elsa Studio module](#elsa-studio-module-117)).
- #116 depends on #219 (`OperationStatus` on after-hook contexts) and adds the archive and restore hook points.
- #118 builds `Aster.Elsa.Agents` and depends on #222, so `AgentConfig.Agents` can map to `reference[]` facets.
- SQL Server hosts depend on #152.
- `Aster.Core` and the provider packages gain no dependency on Elsa. Only the `Aster.Elsa*` packages reference Elsa.
