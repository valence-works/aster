# Elsa Integration

`Aster.Elsa` is an [Elsa Workflows](https://github.com/elsa-workflows/elsa-core) 3 module: activities that work on Aster resources from a workflow, and triggers that start or resume workflows when Aster resources change. It supports Elsa 3.9.0 and later 3.x releases (`[3.9.0, 4.0.0)`), on .NET 8, 9 and 10. The decisions behind it are in the [module design](https://github.com/valence-works/aster/blob/main/docs/consumers/elsa-module-design.md).

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
- **Out of scope:** forms generated from the definition's facets in the designer belong to the Elsa Studio module (#117). Until then, the input is free JSON, validated by Aster on save.

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

- **Start or resume.** As a start activity (`CanStartWorkflow`), a trigger starts a new workflow instance per matching event. Later in a workflow, it waits with a bookmark and resumes once.
- **Filters.** An empty filter matches every value. A trigger only fires for changes in its workflow's Elsa tenant.
- **How it works.** An Aster lifecycle hook sends Elsa a stimulus through `IStimulusSender` after each committed change, in the Elsa tenant of the change (`ITenantAccessor.PushContext`). The stimulus, `ResourceLifecycleStimulus(TenantId, DefinitionId, Channel)`, hashes like the trigger's indexed payload and its bookmark, so only workflows whose filters fit start or resume. The package README has the [full contract](https://github.com/valence-works/aster/blob/main/src/integrations/Aster.Elsa/README.md#stimulus-contract).
- **Only committed changes fire.** The hook sends only when the after hook's `OperationStatus` is `Succeeded`; a failed write fires nothing.
- **Inline.** Matching workflows run in the flow of the Aster call, until they finish or suspend.

### Delivery is at-most-once

- **Never fails the write.** A failing send is logged as a warning with the resource, version, channel and tenant. The Aster write stays committed, and its caller doesn't see the failure.
- **No retry.** A retry could start a workflow twice.
- **No outbox.** If the process stops between the commit and the send, the event is lost. A durable outbox would mean persisting events in Aster, which ADR 0001 keeps out of scope.

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

- **Without multitenancy:** if Elsa's multitenancy services aren't registered, every call uses the default scope.
- **Queries:** a `ResourceQuery` that names a different tenant faults with `tenant-scope-mismatch`.
- **Host code:** `AsterTenantMapping.FromElsaTenantId` and `FromTenantAccessor` expose the same mapping.

## Notes

- **Name clashes:**
  - Elsa's `Activity.Version` is the activity type's version, so the activities call the resource version `ResourceVersion`.
  - Elsa has its own `TenantScope` type (`Elsa.Common.Multitenancy`). Alias Aster's when a file imports both namespaces.
- **Namespace shadowing:** inside a namespace under `Aster`, the name `Elsa` resolves to `Aster.Elsa`. Put Elsa `using` directives at the top of the file, or write `global::Elsa`.
- **Dependencies:** the package depends on `Elsa.Workflows.Runtime`. Through Elsa's own dependencies, that brings in the ASP.NET Core shared framework. The package doesn't depend on `Aster.AspNetCore`.
