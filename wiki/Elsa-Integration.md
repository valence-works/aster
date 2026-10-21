# Elsa Integration

`Aster.Elsa` is an [Elsa Workflows](https://github.com/elsa-workflows/elsa-core) 3 module: activities that work on Aster resources from a workflow, and triggers that start or resume workflows when Aster resources change. [`Aster.Elsa.Agents`](#agent-definitions) stores Elsa's agent definitions on Aster, and `Aster.Elsa.Studio` is the [Elsa Studio](#elsa-studio) counterpart: resource lists and editors in the Studio. All three support Elsa 3.9.0 and later 3.x releases (`[3.9.0, 4.0.0)`), on .NET 8, 9 and 10. The decisions behind them are in the [module design](https://github.com/valence-works/aster/blob/main/docs/consumers/elsa-module-design.md).

## Registration

The host registers Aster, with the provider that matches its Elsa database, and enables the module:

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterPostgresJsonb(options => options.ConnectionString = connectionString);

builder.Services.AddElsa(elsa => elsa.UseAster());
```

- **Two feature systems, one registration.** `UseAster()` enables `Aster.Elsa.Features.AsterFeature` in Elsa's classic feature system. Shell-based (CShells) hosts enable the `Aster` shell feature, `Aster.Elsa.ShellFeatures.AsterFeature`. Both call the same internal registration.
- **Dependencies.** The feature depends on Elsa's workflow management feature (which registers activities) and workflow runtime feature.
- **The provider is the host's choice.** `UseAster()` registers the activities, the triggers and the lifecycle hook that feeds them. It doesn't pick an Aster provider or read Elsa's connection string.

## Activities

| Activity | Purpose |
|---|---|
| Create Resource | Create version 1 of a resource from a definition ID and a JSON object of aspect payloads |
| Update Resource | Save a new version on top of `BaseVersion`; faults with `concurrency-conflict` when it is no longer the latest |
| Get Resource | Read the latest version, a specific version (`ResourceVersion`), or the version active in a `Channel`; the result is empty when there is none |
| Query Resources | Run a portable `ResourceQuery`, hiding archived and soft-deleted resources unless `IncludeArchived` is set |
| Activate Version | Activate a version in a channel, replacing what is active unless `AllowMultipleActive` is set; activating an older version is a rollback |
| Deactivate Version | Deactivate a version in a channel |
| Promote Resource | Activate the version active in `SourceChannel` in `TargetChannel` |
| Archive Resource | Mark a resource archived, timestamped with Elsa's system clock |

Results are Aster's own types (`Resource`, `IReadOnlyList<Resource>`, `ResourceLifecycleMarker`), so a later activity can use them in expressions, for example `created.Get(context)!.Version` as the `BaseVersion` of an update. The package README has a [complete sample workflow](https://github.com/valence-works/aster/blob/main/src/integrations/Aster.Elsa/README.md#sample-workflow).

### Aspects input

Create Resource and Update Resource take `Aspects`, a JSON object keyed by aspect key, as `POST /resources` of [ASP.NET Core Endpoints](AspNetCore-Endpoints) does:

```json
{ "Title": { "Title": "Gadget" }, "Price": { "Amount": 10 } }
```

- **Accepted shapes:**
  - JSON text, which is what the designer's JSON editor stores
  - `JsonElement` or `JsonObject`
  - a dictionary or an `ExpandoObject`, as JavaScript expressions return
  - any other object that serializes to a JSON object

  Member names are kept as written, because aspect keys and facet IDs are case-sensitive.
- **Updates:** Update Resource replaces each aspect it names whole and keeps the others.
- **Free JSON:** in the workflow designer the input is free JSON, validated by Aster on save. Forms generated from a definition's facets are in the [Elsa Studio module](#elsa-studio)'s resource editor, not in activity inputs.

### Archived resources

Query Resources excludes archived and soft-deleted resources by default, as the list endpoints of `Aster.AspNetCore` do (spike decision F12):

- **`IncludeArchived`** returns every lifecycle state.
- **A query that sets `LifecycleState` itself** keeps it. For example, `LifecycleState = Archived` returns only archived resources.
- **Get Resource** reads by ID and returns archived resources like any other.

### Promotion

Promote Resource is two explicit calls, a read of the active version and an activation, rather than a core API (spike decision F7). It faults instead of guessing:

- **`promote-source-not-active`:** no version is active in the source channel.
- **`promote-source-ambiguous`:** several versions are active there, as in an A/B test. Activate the one to promote with Activate Version.

## Triggers

| Trigger | Fires when | Filters |
|---|---|---|
| Resource Saved | A version is saved: created, updated or schema-upgraded | `DefinitionId` |
| Resource Activated | A version is activated in a channel | `DefinitionId`, `Channel` |
| Resource Deactivated | A version is deactivated from a channel | `DefinitionId`, `Channel` |
| Resource Archived | A resource is archived (not soft-deleted) | `DefinitionId` |

The result is a `ResourceLifecycleEvent`: kind, Elsa tenant, resource ID, definition ID, version, channel, save kind and operation ID.

- **Start or resume.** As a start activity (`CanStartWorkflow`), a trigger starts a new workflow instance per matching event. Later in a workflow, it waits with a bookmark and resumes once, on the next matching event: never on one the instance had already received.
- **Filters.** An empty filter matches every value. A trigger only fires for changes in its workflow's Elsa tenant.
- **How it works.** An Aster lifecycle hook queues each committed change, and a background worker sends Elsa a stimulus through `IStimulusSender`, in the Elsa tenant of the change (`ITenantAccessor.PushContext`). The stimulus, `ResourceLifecycleStimulus(TenantId, DefinitionId, Channel)`, hashes like the trigger's indexed payload and its bookmark, so only workflows whose filters fit start or resume. The package README has the [full contract](https://github.com/valence-works/aster/blob/main/src/integrations/Aster.Elsa/README.md#stimulus-contract).
- **Only committed changes fire.** The hook sends only when the after hook's `OperationStatus` is `Succeeded`; a failed write fires nothing.
- **In the background.** The Aster call returns once the change is committed. Matching workflows run afterwards, one event at a time, in commit order, so a resumed workflow can save a resource that matches its own trigger without waiting on Elsa's resume lock.

### Delivery is at-most-once

- **Never fails the write.** A failing send is logged as a warning with the resource, version, channel and tenant. The Aster write stays committed, and its caller doesn't see the failure.
- **No retry.** A retry could start a workflow twice.
- **No outbox.** The queue is in memory: if the process stops between the commit and the send, the event is lost. A durable outbox would mean persisting events in Aster, which ADR 0001 keeps out of scope.

Hosts that need every change handled should reconcile from Aster's state, for example with a scheduled workflow that queries for what it missed.

### Archive and restore hooks

Resource Archived needed a lifecycle hook point for archiving. `IResourceLifecycleHook` has `OnBeforeArchiveAsync`/`OnAfterArchiveAsync` and `OnBeforeRestoreAsync`/`OnAfterRestoreAsync`, as default interface members, with a `ResourceArchiveLifecycleContext`. Any host hook can use them, for example to audit archives. They run around every lifecycle marker write and clear: the marker service, policy application and the restore service. The archive points cover soft-deletes too (`context.Marker.State`).

## Faults

A failing activity throws `AsterActivityException`. Elsa faults the activity and records an incident. The incident's exception metadata (`ExceptionState.Metadata`) carries:

- **`code`:** the stable code, which is also `AsterActivityException.Code`.
- **`diagnostics`:** a JSON array of every diagnostic, each with `code` and `message`, plus `aspectKey`, `facetId`, `elementIndex`, `path` and `feature` where they apply.

The codes are the ones `Aster.AspNetCore` returns for the same failures. For example, a stale `BaseVersion` is `concurrency-conflict` both as a workflow fault and as an HTTP `409`. A facet validation failure lists every `facet-value-*` diagnostic, as the HTTP `400` does. `AsterActivityFaultCodes` lists the codes the activities add, such as `resource-not-found`, `aspects-invalid` and `promote-source-ambiguous`.

## Tenants

The module maps Elsa's current tenant to an Aster tenant scope on every call, so one host serves every tenant:

| Elsa tenant ID | Aster scope |
|---|---|
| `""` (`Tenant.DefaultTenantId`) | `TenantScope.Default` |
| any other ID | `TenantScope.FromTenantId(id)` |
| `"*"` (`Tenant.AgnosticTenantId`) | refused: `tenant-scope-invalid` |
| `"default"` | refused: `tenant-scope-invalid`, because it would share Aster's default scope with Elsa's default tenant |
| only whitespace, or leading or trailing whitespace | refused: `tenant-scope-invalid` |

- **Without multitenancy:** if Elsa's multitenancy services aren't registered, every call uses the default scope.
- **Queries:** a `ResourceQuery` that names a different tenant faults with `tenant-scope-mismatch`.
- **Host code:** `AsterTenantMapping.FromElsaTenantId` and `FromTenantAccessor` expose the same mapping.

## Agent definitions

`Aster.Elsa.Agents` stores Elsa's agent definitions on Aster. It is an `IAgentStore` for Elsa's agent persistence feature, plugged into Elsa's own `UseAgentStore` seam, so Elsa is unchanged. The [package README](https://github.com/valence-works/aster/blob/main/src/integrations/Aster.Elsa.Agents/README.md) has the details, and the [results write-up](https://github.com/valence-works/aster/blob/main/docs/consumers/elsa-agents-results.md) has the measurements.

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterPostgresJsonb(options => options.ConnectionString = connectionString);

builder.Services.AddElsa(elsa => elsa
    .UseAgents()
    .UseAgentPersistence(agents => agents.UseAsterAgentStore(options => options.Channel = "production")));
```

- **Opt-in is the flag.** Without `UseAsterAgentStore`, Elsa keeps its own store. Don't also enable Elsa's EF Core agent store on the same feature: it selects itself again while Elsa applies features.
- **Storage:** each agent is an `ElsaAgent` resource whose ID is the Elsa agent ID, with an `Agent` aspect (name, description) and an `AgentConfig` aspect (Elsa's `AgentConfig`).
  - Name, description, function name, prompt and skills (`string[]`) are facets, so agents can be queried.
  - `AgentConfig.Agents` is a `reference[]` facet. The store translates Elsa's agent names to resource IDs on save and back on read, so references follow renames and have a reverse lookup.
- **Versions and channels:**
  - Every save is a new version.
  - Reads return, for each agent, the version active in `Channel` (default `production`).
  - With `AutoActivate` (default `true`) every save is activated there at once. With `false` saves are drafts until activated through Aster, which is promotion; activating an older version is rollback.
- **Deletes** soft-delete: history stays, and Aster's restore service brings an agent back.
- **Tenants** map as for the activities above.
- **Migration:** `IAsterAgentImporter.ImportAsync(source)` copies agents from any `IAgentStore`, such as Elsa's EF Core store, into Aster.
  - It keeps their IDs and activates them.
  - It is idempotent: agents that already exist in Aster are skipped.
  - Referenced agents are imported first; reference cycles are saved in two versions.
- **Feature systems:** classic only. Elsa 3.9's agent persistence has no CShells shell feature.

## Elsa Studio

`Aster.Elsa.Studio` is an Elsa Studio 3 (Blazor) module for working with resources in the Studio (#117). It supports Elsa Studio 3.9.0 and later 3.x releases on .NET 8, 9 and 10, in Blazor Server and WebAssembly hosts. It talks to the [ASP.NET Core Endpoints](AspNetCore-Endpoints) over HTTP, so the Elsa server maps them with `MapAster`.

```csharp
// Elsa Studio host, next to its other modules (AddWorkflowsModule registers Elsa Studio's UI-hint editors).
builder.Services
    .AddAsterStudioModule(new Uri("https://elsa-server.example/api/aster"))
    .AddHttpMessageHandler<AuthenticatingApiHttpMessageHandler>();
```

- **Pages:**
  - **Resources** in the menu (`aster/definitions`) lists every definition.
  - Each definition lists its resources, with search, a lifecycle filter and paging.
  - The resource editor has three tabs:
    - **Edit:** saves a new version on top of the loaded one; a stale edit shows `concurrency-conflict` with a reload.
    - **Versions:** views any version read-only, activates a version in a channel (a rollback when it is older) and deactivates one.
    - **Referenced by:** lists the resources whose reference facets name this one.
- **No code per type.** A definition registered at runtime appears on the next visit to the list and is editable at once: the form comes from `GET /definitions/{id}/schema` ([JSON Schema Export](Json-Schema-Export)).
- **Editors.** Each facet's `x-aster-uiHint` picks one of Elsa Studio's own UI-hint editors through `IUIHintService`, and facets without a usable hint fall back by type:
  - `text`, `slug`, `email`, `url`, `secret` and `number` → `singleline`;
  - `multiline`, `markdown`, `html` → `multiline`;
  - `select` → `dropdown` (scalar) or `checklist` (array);
  - `radio` → `radiolist`;
  - `checkbox` and `switch` → `checkbox`, or `checklist` on an array;
  - `date`, `datetime` → `datetime-picker`;
  - `tags` → `multitext`;
  - `json`, `object` and legacy facets → `json-editor`;
  - `code` → `code-editor`.

  The [package README](https://github.com/valence-works/aster/blob/main/src/integrations/Aster.Elsa.Studio/README.md#forms) has the full table.
- **Reference picker.** `reference` and `reference[]` facets use the module's `aster-reference-picker` editor. It searches the facet's target definitions with `POST /resources/query`, filtering resource IDs with `contains` or `startsWith`, whichever `GET /resources/query/capabilities` declares. The **Referenced by** tab runs the reverse lookup the same way, with a `references` filter on each reference facet that may point to the resource.
- **Diagnostics on fields.** Save diagnostics (`aspectKey`, `facetId`, `elementIndex`) show under the field at `aspects.{aspectKey}.{facetId}`. The resource list validates its query with `POST /resources/query/validate` first and shows each diagnostic under the input its `path` names. Everything else shows as an alert.
- **Tenants and authorization stay on the server.** The module sends the host's credentials; the Aster endpoints resolve the tenant (`ITenantScopeAccessor`) and authorize.
- **Not included:** a sample Studio host, definition editing, and archive, restore or schema-upgrade actions, which `Aster.AspNetCore` doesn't expose yet.

## Notes

- **Name clashes:**
  - Elsa's `Activity.Version` is the activity type's version, so the activities call the resource version `ResourceVersion`.
  - Elsa has its own `TenantScope` type (`Elsa.Common.Multitenancy`). Alias Aster's when a file imports both namespaces.
- **Namespace shadowing:** inside a namespace under `Aster`, the name `Elsa` resolves to `Aster.Elsa`. Put Elsa `using` directives at the top of the file, or write `global::Elsa`.
- **Dependencies:**
  - `Aster.Elsa` depends on `Elsa.Workflows.Runtime`, which, through Elsa's own dependencies, brings in the ASP.NET Core shared framework.
  - `Aster.Elsa.Agents` adds `Elsa.Agents.Persistence`, which brings Elsa's agents core and its Semantic Kernel dependencies.
  - `Aster.Elsa.Studio` depends on `Elsa.Studio.UIHints` (which brings Elsa Studio's core and shared packages, and MudBlazor 9) and on nothing from Aster: it reaches Aster only over HTTP.
  - None of them depends on `Aster.AspNetCore`.
