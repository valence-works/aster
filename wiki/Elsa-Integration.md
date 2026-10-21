# Elsa Integration

`Aster.Elsa` is an [Elsa Workflows](https://github.com/elsa-workflows/elsa-core) 3 module: activities that work on Aster resources from a workflow. `Aster.Elsa.Studio` is its [Elsa Studio](#elsa-studio) counterpart: resource lists and editors in the Studio. Both support Elsa 3.9.0 and later 3.x releases (`[3.9.0, 4.0.0)`), on .NET 8, 9 and 10. The decisions behind them are in the [module design](https://github.com/valence-works/aster/blob/main/docs/consumers/elsa-module-design.md).

## Registration

The host registers Aster, with the provider that matches its Elsa database, and enables the module:

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterPostgresJsonb(options => options.ConnectionString = connectionString);

builder.Services.AddElsa(elsa => elsa.UseAster());
```

- **Two feature systems, one registration.** `UseAster()` enables `Aster.Elsa.Features.AsterFeature` in Elsa's classic feature system. Shell-based (CShells) hosts enable the `Aster` shell feature, `Aster.Elsa.ShellFeatures.AsterFeature`. Both call the same internal registration.
- **Dependencies.** The feature depends on Elsa's workflow management feature (which registers activities) and workflow runtime feature.
- **The provider is the host's choice.** `UseAster()` registers the activities only. It doesn't pick an Aster provider or read Elsa's connection string.

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
- **Dependencies:** the package depends on `Elsa.Workflows.Runtime`. Through Elsa's own dependencies, that brings in the ASP.NET Core shared framework. The package doesn't depend on `Aster.AspNetCore`.
- **Studio dependencies:** `Aster.Elsa.Studio` depends on `Elsa.Studio.UIHints` (which brings Elsa Studio's core and shared packages, and MudBlazor 9) and on nothing from Aster: it reaches Aster only over HTTP.
