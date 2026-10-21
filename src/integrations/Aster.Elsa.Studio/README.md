# Aster.Elsa.Studio

An [Elsa Studio](https://github.com/elsa-workflows/elsa-studio) 3 module for [Aster](https://github.com/valence-works/aster). It lists resource definitions and their resources, and edits resources in forms generated from each definition's JSON Schema export. It also shows version history with channel activation. A resource type defined at runtime is editable as soon as it is registered, with no code changes.

**Supported Elsa Studio versions:** 3.9.0 and later 3.x releases (`[3.9.0, 4.0.0)`). Targets .NET 8, 9 and 10, the frameworks Elsa Studio 3.9 ships. Works in Blazor Server and Blazor WebAssembly hosts.

## Register

The module talks to the [`Aster.AspNetCore`](https://github.com/valence-works/aster/tree/main/src/hosting/Aster.AspNetCore) endpoints over HTTP, so the server maps them, usually in the Elsa server host:

```csharp
// Elsa server
builder.Services.AddAsterCore();
builder.Services.AddAsterSqlServerJson(options => options.ConnectionString = connectionString);
app.MapAster("/api/aster").RequireAuthorization();
```

The Elsa Studio host adds the module next to Elsa's own modules:

```csharp
// Elsa Studio host (Blazor Server or WebAssembly)
using Aster.Elsa.Studio;
using Elsa.Studio.Login.HttpMessageHandlers;
using Elsa.Studio.Workflows.Extensions;

// ... the host's existing Elsa Studio registrations (shell, remote backend, login), including:
builder.Services.AddWorkflowsModule();               // registers Elsa Studio's UI-hint editors
builder.Services
    .AddAsterStudioModule(new Uri("https://elsa-server.example/api/aster"))
    .AddHttpMessageHandler<AuthenticatingApiHttpMessageHandler>(); // Elsa.Studio.Login's handler, or the host's own
```

- **`AddAsterStudioModule`** registers the module's Elsa Studio feature (its assembly joins the Studio router), the **Resources** menu entry and the reference picker editor. It returns the `IHttpClientBuilder` of the module's `HttpClient`, so the host adds the same authentication handler it uses for the Elsa API.
- **Transport.** Use an `https` address for any endpoint reached over a network you don't control. The module sends resource content, writes and the host's credentials to that address, and doesn't refuse `http`, so that loopback and in-cluster hosts keep working.
- **Authentication.** `Elsa.Studio.Login.HttpMessageHandlers.AuthenticatingApiHttpMessageHandler` is the handler Elsa Studio's login module registers for the Elsa API. A host with its own authentication adds its own handler instead.
- **UI-hint editors.** Forms render through Elsa Studio's `IUIHintService`, so the host needs Elsa Studio's default UI-hint handlers (`AddDefaultUIHintHandlers`). `AddWorkflowsModule` registers them.
- **Tenants and authorization are the server's.** The module sends the host's credentials and nothing else. The Aster server resolves the tenant (`ITenantScopeAccessor`) and authorizes each request.
- **JSON.** The module reads and writes Aster's default wire format: camelCase property names, as `MapAster` uses unless the server replaces `SerializerOptions`. Enum names may use any naming policy.

## Pages

| Route | Page |
|---|---|
| `aster/definitions` | Every resource definition (latest version), read on each visit, with links to its resources and to a new resource |
| `aster/definitions/{definitionId}` | The definition's resources: search by resource ID, lifecycle filter (active, archived, soft-deleted, all), paging |
| `aster/definitions/{definitionId}/new` | A new resource, in a form generated from the definition's schema |
| `aster/resources/{resourceId}` | The resource editor: **Edit** (saves a new version on top of the loaded one), **Versions** (view any version read-only, activate a version in a channel, deactivate by closing a channel chip) and **Referenced by** (the resources whose reference facets name this one) |

## Forms

Each form comes from `GET /definitions/{id}/schema` (the JSON Schema export, #106): one section per aspect and one field per facet, in display order. The definition version is the one the resource was saved against. The module then picks each field's editor from its `x-aster-uiHint` (#107). The editor is one of Elsa Studio's own UI-hint editors, resolved through `IUIHintService`:

| `x-aster-uiHint` | Elsa Studio editor |
|---|---|
| `text`, `slug`, `email`, `url`, `secret` | `singleline` |
| `number` (`int`, `decimal`) | `singleline`, parsed as a number |
| `multiline`, `markdown`, `html` | `multiline` |
| `code` | `code-editor` |
| `json` | `json-editor` |
| `select` | `dropdown` (scalar) or `checklist` (array), over the allowed values |
| `radio` | `radiolist` |
| `checkbox` | `checkbox` (`bool`) or `checklist` (array) |
| `switch` | `checkbox` |
| `date`, `datetime` | `datetime-picker` (a date is stored at midnight UTC) |
| `tags` | `multitext` |
| `reference-picker` | `aster-reference-picker`, the module's own editor |

**Fallback per type.** A facet with no hint, an unknown hint, or a hint that doesn't fit its type gets the editor for its type:

| Facet type | Editor |
|---|---|
| any type with allowed values (except `bool`) | `dropdown`, or `checklist` for an array |
| `string`, `guid`, `int`, `decimal` | `singleline` |
| `bool` | `checkbox` |
| `datetime` | `datetime-picker` |
| `reference`, `reference[]` | `aster-reference-picker` |
| `object`, legacy types | `json-editor` |
| other arrays | `multitext`, or `json-editor` for `object[]` |

- **Elsa's editors, unwrapped.** Each facet is described to the editor as an unwrapped input (`InputDescriptor.IsWrapped = false`), so the editor shows no expression syntax picker and writes a plain value. The module converts it to the facet's JSON type. A value that doesn't fit, such as `abc` for a `decimal`, is shown on the field and blocks the save.
- **Facets the form doesn't know** are kept on save, because an update replaces each aspect it names whole.

## Reference picker

`reference` and `reference[]` facets render the module's `ReferencePicker`, registered as the Elsa Studio UI-hint handler `aster-reference-picker`. It searches the facet's `targetDefinitionIds` (`x-aster-reference`) through `POST /resources/query`: one query per target definition (or one over every definition when the facet names none), filtering `ResourceId` with `contains` or `startsWith`, whichever `GET /resources/query/capabilities` declares. Without either, it filters a page itself. A typed ID that matches nothing is still accepted, and Aster validates it on save.

The **Referenced by** tab is the reverse lookup. For each definition with a reference facet that may point to this resource's definition, it runs `POST /resources/query` with a `references` filter on that facet.

## Diagnostics

Server diagnostics land on the fields they concern, by request-body path:
- **Saves.** A `400` from `POST /resources` or `PUT /resources/{id}` lists diagnostics with `aspectKey`, `facetId` and `elementIndex`. Each one shows under the field at `aspects.{aspectKey}.{facetId}`, with the element number for an array element. An aspect-level diagnostic shows on its section.
- **Queries.** The resource list checks its query with `POST /resources/query/validate` (#194) before running it. Each diagnostic's `path` (`filter...`, `lifecycle`, `take`) shows under the input that produced it.
- **Anything else** shows as an alert above the form. That covers a diagnostic without a field, such as an identifier with surrounding whitespace, and a `409` such as `concurrency-conflict` (which offers a reload).

## Not included

- No sample Elsa Studio host: wire the module into an existing one as shown above.
- No definition or aspect editor: definitions are registered through the API or Aster Studio.
- No archive, restore or schema-upgrade actions: `Aster.AspNetCore` doesn't expose them yet.
