# Aster.Elsa

An [Elsa Workflows](https://github.com/elsa-workflows/elsa-core) 3 module for [Aster](https://github.com/valence-works/aster): activities that create, update, read, query, activate, deactivate, promote and archive Aster resources from a workflow, and triggers that start or resume workflows when resources are saved, activated, deactivated or archived.

**Supported Elsa versions:** 3.9.0 and later 3.x releases (`[3.9.0, 4.0.0)`). Targets .NET 8, 9 and 10.

## Register

The host registers Aster itself, with the provider that matches its Elsa database, then enables the module in Elsa:

```csharp
using Aster.Core.Extensions;
using Aster.Elsa;

builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options => options.ConnectionString = connectionString); // or AddAsterPostgresJsonb

builder.Services.AddElsa(elsa => elsa.UseAster());
```

Shell-based (CShells) hosts enable the `Aster` shell feature instead (`Aster.Elsa.ShellFeatures.AsterFeature`). Both features call the same registration: the activities, the triggers, and the Aster lifecycle hook that feeds the triggers. The module depends on Elsa's workflow management and workflow runtime features.

## Activities

All activities are in the **Aster** category.

| Activity | Inputs | Result |
|---|---|---|
| **Create Resource** | `DefinitionId`, `ResourceId` (optional), `Aspects` | The new version 1 (`Resource`) |
| **Update Resource** | `ResourceId`, `BaseVersion`, `Aspects` | The new version (`Resource`) |
| **Get Resource** | `ResourceId`, then at most one of `ResourceVersion` and `Channel` | The latest version, that version, or the version active in the channel (the highest when several are); empty when there is none |
| **Query Resources** | `Query` (a `ResourceQuery`), `DefinitionId`, `IncludeArchived` | The matching versions (`IReadOnlyList<Resource>`) |
| **Activate Version** | `ResourceId`, `ResourceVersion`, `Channel`, `AllowMultipleActive` | The activated version |
| **Deactivate Version** | `ResourceId`, `ResourceVersion`, `Channel` | The deactivated version |
| **Promote Resource** | `ResourceId`, `SourceChannel`, `TargetChannel`, `AllowMultipleActive` | The version active in the source channel, now also active in the target channel |
| **Archive Resource** | `ResourceId`, `Reason` | The archive marker (`ResourceLifecycleMarker`) |

- **Aspects** are a JSON object keyed by aspect key, such as `{ "Title": { "Title": "Gadget" } }`, as in `POST /resources` of `Aster.AspNetCore`. The input takes JSON text (the designer's JSON editor), a `JsonElement` or `JsonObject`, a dictionary or `ExpandoObject` (from a JavaScript expression), or any object that serializes to a JSON object. Member names are kept as written. Update Resource replaces each named aspect whole and keeps the others.
- **Concurrency:** Update Resource saves only when `BaseVersion` is still the latest version. Otherwise it faults with `concurrency-conflict` and writes nothing.
- **Archived resources are hidden:** Query Resources excludes archived and soft-deleted resources unless `IncludeArchived` is set or the query sets `LifecycleState` itself, as the list endpoints of `Aster.AspNetCore` do. Get Resource reads by ID and returns them like any other resource.
- **Promote** is two explicit calls: it reads the version active in the source channel and activates it in the target channel. It faults with `promote-source-not-active` when no version is active there and with `promote-source-ambiguous` when several are.
- **Typed inputs.** `Query` takes a `ResourceQuery`, so it is set from code or a C# expression. Designer UI for definition-driven inputs (forms built from a definition's facets) belongs to the Elsa Studio module (#117).

## Triggers

Triggers start a workflow, or resume one that waits, when Aster commits a change. All are in the **Aster** category, and their result is a `ResourceLifecycleEvent`: `Kind`, `TenantId` (the Elsa tenant), `ResourceId`, `DefinitionId`, `ResourceVersion`, `Channel`, `SaveKind` and `OperationId`.

| Trigger | Fires when | Filters |
|---|---|---|
| **Resource Saved** | A version is saved: created, updated or schema-upgraded (`SaveKind` says which) | `DefinitionId` |
| **Resource Activated** | A version is activated in a channel, including through Promote Resource | `DefinitionId`, `Channel` |
| **Resource Deactivated** | A version is deactivated from a channel | `DefinitionId`, `Channel` |
| **Resource Archived** | A resource is archived: by Archive Resource, `IResourceLifecycleMarkerService` or policy application. Soft-deletes don't fire it | `DefinitionId` |

- **Start or resume.** As a workflow's start activity (`CanStartWorkflow`), a trigger starts a new instance for every matching event. Placed later in a workflow, it waits with a bookmark and resumes once, on the next matching event. An event the instance had already received when the trigger started waiting doesn't resume it, so a workflow started by an event waits for the next one, even though the same event also reaches its bookmark (through another stimulus, or Elsa's bookmark queue).
- **Filters.** An empty filter matches every value. Filters are read when Elsa indexes the workflow (on publish) or creates the bookmark, so use literals or expressions that don't depend on the event.
- **Tenants.** A trigger only fires for changes in the Elsa tenant its workflow runs in, mapped as in [Tenants](#tenants): an Aster write in `TenantScope.Default` fires workflows in Elsa's default tenant. Changes in an Aster tenant that has no Elsa tenant fire nothing.
- **Only committed changes fire.** A save, activation, deactivation or archive that fails fires nothing.
- **In the background.** The Aster call returns once the change is committed; it doesn't wait for the workflows. The module queues each event in memory, and one background worker sends the events to Elsa one at a time, in the order they were committed, outside the Aster caller's execution context, in the Elsa tenant of the change. So a workflow resumed by an event can save a resource that matches the same trigger: its own event is sent after the resumed run ends. Sending inline would wait on the lock Elsa holds for that stimulus during the run, until it timed out. A workflow that saves a resource matching its own start trigger fires itself again.

### Delivery is at-most-once

Each committed change is queued and sent to Elsa once, shortly after the commit. Nothing is persisted or retried:

- **A failing send never fails the Aster write.** The module logs a warning with the trigger, resource, version, channel and tenant, and the caller and other lifecycle hooks don't see the failure. The change stays committed.
- **No retry.** Retrying could start a workflow twice, so a failed send is not repeated.
- **Lost on a stop or crash.** The queue is in memory. Events still queued when the process stops or crashes are lost.

A host that needs every change delivered should reconcile from Aster's state, for example with a scheduled workflow that queries for what it missed.

Elsa queues a stimulus that resumes no bookmark for `BookmarkQueuePurgeOptions.Ttl` (10 minutes by default), so a matching bookmark created in that window resumes on the earlier event. This is Elsa's behaviour for every stimulus.

### Example

Start a payment workflow when an invoice is approved:

```csharp
public class PayApprovedInvoice : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var approved = builder.WithVariable<ResourceLifecycleEvent>("Approved", default!);

        builder.Root = new Sequence
        {
            Activities =
            {
                new ResourceActivated
                {
                    CanStartWorkflow = true,
                    DefinitionId = new("Invoice"),
                    Channel = new("approved"),
                    Result = new(approved),
                },
                new GetResource
                {
                    ResourceId = new(context => approved.Get(context)!.ResourceId),
                    ResourceVersion = new(context => approved.Get(context)!.ResourceVersion),
                },
                // ... pay the invoice
            },
        };
    }
}
```

### Stimulus contract

Hosts that send their own events, or inspect bookmarks, use the same contract:

- **Stimulus.** `ResourceLifecycleStimulus(TenantId, DefinitionId, Channel)`, sent with `IStimulusSender.SendAsync` under the trigger's activity type name, such as `Aster.ResourceActivated`. `TenantId` is the Elsa tenant ID; `null` stands for an empty filter.
- **One send per filter combination.** Elsa matches hashes exactly, so the module sends one stimulus per combination of the event's definition ID and channel with `null`: two for Saved and Archived, four for Activated and Deactivated. Each trigger matches exactly one.
- **Event.** The `ResourceLifecycleEvent` travels as workflow input under `ResourceLifecycleEvent.WorkflowInputKey`.

## Faults

A failing activity faults with `AsterActivityException`. Elsa records it as an activity incident whose exception metadata carries `code` and, as a JSON array, `diagnostics` (each with `code`, `message` and, where they apply, `aspectKey`, `facetId`, `elementIndex`, `path` and `feature`). Codes are Aster's own and match what `Aster.AspNetCore` returns over HTTP:

| Code | When |
|---|---|
| `concurrency-conflict` | Update Resource's `BaseVersion` is no longer the latest version |
| `facet-value-*` (for example `facet-value-required`) | An aspect payload fails the definition's facet validation; every diagnostic is listed |
| `resource-not-found`, `version-not-found`, `definition-not-found` | The resource, version or definition does not exist in the current tenant |
| `duplicate-resource-id`, `singleton-violation` | Create Resource conflicts with an existing resource |
| query validation codes (for example `negative-take`) | The query is not valid for the active provider; every failure is listed |
| `lifecycle-marker-conflict` | Archive Resource on a soft-deleted resource |
| `lifecycle-hook-rejected`, `lifecycle-hook-failed` | A host lifecycle hook rejected or failed the operation |
| `tenant-scope-invalid`, `tenant-scope-mismatch` | The current Elsa tenant has no Aster scope, or a query names another tenant |
| `identifier-whitespace` | A resource ID, definition ID or channel the activity would write starts or ends with whitespace |
| `aspects-invalid`, `resource-id-required`, `definition-id-required`, `channel-required`, `resource-selector-invalid`, `promote-source-not-active`, `promote-source-ambiguous` | An input is missing or invalid (see `AsterActivityFaultCodes`) |

## Tenants

Every call maps Elsa's current tenant (`ITenantAccessor.TenantId`) to an Aster tenant scope, at the time of the call:

| Elsa tenant ID | Aster scope |
|---|---|
| `""` (Elsa's default tenant) | `TenantScope.Default` |
| any other ID | `TenantScope.FromTenantId(id)`, the same ID |
| `*` (agnostic), `default`, only whitespace, or starting or ending with whitespace | refused: the activity faults with `tenant-scope-invalid` |

Without Elsa's multitenancy services every call uses the default scope. `AsterTenantMapping` exposes the same mapping to host code.

## Sample workflow

Create a product, save a new version on top of the one just created, and publish it. Each activity's result feeds the next one's inputs through expressions.

The activities work on definitions the host has registered, so register `Product` first, for example at startup. Otherwise `CreateResource` faults with `definition-not-found`:

```csharp
var definitions = app.Services.GetRequiredService<IResourceDefinitionStore>();
await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect("Title").WithFacet("Title", "string", isRequired: true)
    .WithAspect("Price").WithFacet("Amount", "decimal")
    .Build());
```

The workflow:

```csharp
using Aster.Core.Models.Instances;
using Aster.Elsa.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Memory;

public class PublishProduct : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var created = builder.WithVariable<Resource>("Created", default!);
        var updated = builder.WithVariable<Resource>("Updated", default!);

        builder.Root = new Sequence
        {
            Activities =
            {
                new CreateResource
                {
                    DefinitionId = new("Product"),
                    Aspects = new("""{ "Title": { "Title": "Gadget" }, "Price": { "Amount": 10 } }"""),
                    Result = new(created),
                },
                new UpdateResource
                {
                    ResourceId = new(context => created.Get(context)!.ResourceId),
                    BaseVersion = new(context => created.Get(context)!.Version),
                    Aspects = new("""{ "Price": { "Amount": 12 } }"""),
                    Result = new(updated),
                },
                new ActivateResourceVersion
                {
                    ResourceId = new(context => updated.Get(context)!.ResourceId),
                    ResourceVersion = new(context => updated.Get(context)!.Version),
                    Channel = new("Published"),
                },
            },
        };
    }
}
```

See the [wiki page](https://github.com/valence-works/aster/blob/main/wiki/Elsa-Integration.md) for more, and the [module design](https://github.com/valence-works/aster/blob/main/docs/consumers/elsa-module-design.md) for the decisions behind it.
