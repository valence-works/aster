# ASP.NET Core Endpoints

`Aster.AspNetCore` maps Aster's REST surface onto a host app, so hosts don't re-implement it. Aster Studio and Elsa Studio are the first two such hosts.

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options => options.ConnectionString = "Data Source=aster.db");

// The host owns authorization; register every policy named below.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("aster-admin", policy => policy.RequireRole("aster-admin")));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapAster("/api/aster", options =>
{
    options.AuthorizationPolicies[AsterEndpointGroups.Definitions] = "aster-admin";
}).RequireAuthorization();
```

`MapAster` returns the route group that holds every endpoint, so host conventions (authorization, rate limiting, CORS) apply with the usual `RouteGroupBuilder` calls.

## Endpoints

| Method and path | Purpose |
|---|---|
| `GET /definitions` | The latest version of each resource definition |
| `GET /definitions/{id}[?version=]` | One definition: the latest version, or a specific one |
| `GET /definitions/{id}/schema[?version=]` | The definition as JSON Schema 2020-12 ([JSON Schema Export](Json-Schema-Export)) |
| `POST /definitions[?expectedLatestVersion=]` | Register a new definition version; with `expectedLatestVersion`, only if the latest version is still that one (`0`: must not exist), otherwise `409` |
| `GET /aspects` | The latest version of each shared aspect definition |
| `GET /aspects/{id}[?version=]` | One shared aspect |
| `GET /aspects/{id}/versions` | Every version of a shared aspect |
| `GET /aspects/{id}/schema[?version=]` | The aspect payload as JSON Schema 2020-12 |
| `POST /aspects[?expectedLatestVersion=]` | Register a new shared aspect version; with `expectedLatestVersion`, only if the latest version is still that one (`0`: must not exist), otherwise `409` (`aspect-version-conflict`) |
| `GET /resources?definitionId=&scope=&channel=&lifecycle=&skip=&take=` | List resources |
| `POST /resources/query` | Query with filters, sorting and paging (see below) |
| `POST /resources/query/validate` | Check a query without running it: `{ isValid, diagnostics }` |
| `GET /resources/query/capabilities` | The active query provider's `QueryCapabilityDescription` (`404` when it declares none) |
| `GET /resources/{id}[?version=]` | One resource: the latest version, or a specific one |
| `GET /resources/{id}/versions` | Version history with active channels and lifecycle state |
| `POST /resources` | Create version 1: `{ definitionId, resourceId?, aspects }` |
| `PUT /resources/{id}` | Save a new version: `{ baseVersion, aspects }` |
| `POST /resources/{id}/activate` / `deactivate` | `{ version, channel, allowMultipleActive? }` |

Shared aspect attachments in `POST /definitions` are pins: for every attachment with `isShared: true`, the endpoint embeds the stored aspect version named by `(aspectDefinitionId, version)`, whatever facets the client sent (ADR 0003). An unknown pin returns `400` with `aspect-definition-not-found`.

## Options

| Option | Default | Effect |
|---|---|---|
| `Groups` | `All` | Which groups to map: `Definitions`, `Aspects`, `Resources` |
| `ReadOnly` | `false` | Map only reading endpoints; `POST /resources/query` stays |
| `AuthorizationPolicies` | none | A policy name per group, applied with `RequireAuthorization` |
| `ConfigureGroup` | none | A callback per mapped group's `RouteGroupBuilder` |
| `DefaultPageSize` / `MaxPageSize` | 50 / 500 | Paging for list and query endpoints |
| `SerializerOptions` | web defaults, camelCase enum names | The JSON options for every request body and response; see [JSON](#json) |

## Errors

Errors are [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) responses, except the `415` routing returns (below):

| Status | When | Extensions |
|---|---|---|
| `400` | Invalid facet values or metadata, unresolved shared aspects, invalid queries, missing (`request-body-required`) or unreadable (`request-body-invalid`) request bodies, a definition ID, aspect definition ID, resource ID, channel or tenant ID with leading or trailing whitespace on create, register or activate (`identifier-whitespace`), rejected schema upgrades (below) | `diagnostics: [{ code, aspectKey, facetId, message, elementIndex, path, feature }]` |
| `403` | Denied by `IAsterResourceAuthorizer` | |
| `404` | Missing definition, aspect, resource or version | |
| `409` | Optimistic concurrency (`concurrency-conflict`), duplicate resource ID, singleton violation, definition or aspect version conflict (`definition-version-conflict`, `aspect-version-conflict`), lifecycle hook veto | `code` |
| `415` | A request body without a JSON `Content-Type`. Routing rejects a non-JSON `Content-Type` before the endpoint runs, with an empty body (add `AddProblemDetails()` and `UseStatusCodePages()` in the host for one); a body with no `Content-Type` reaches the endpoint and gets a problem | |

The package maps no schema upgrade endpoint, but an endpoint a host maps inside an Aster group (through `ConfigureGroup`) gets the same mapping. There, a `ResourceSchemaUpgradeException` from `ResourceSchemaUpgradeResult.ThrowIfRejected()` becomes a problem keyed by its first code: `resource-not-found` is `404`, `concurrency-conflict` is `409` with `code`, and any other code (an invalid target or an invalid upgraded payload) is `400` with every diagnostic.

## Query format

`POST /resources/query` takes a portable query in a wire format that needs no type discriminators:

```json
{
  "definitionId": "Article",
  "scope": "latest",
  "lifecycle": "active",
  "filter": {
    "and": [
      { "exists": "Seo" },
      { "aspect": "Content", "facet": "Views", "op": "range", "value": { "min": 10, "max": 100, "includeMax": false } },
      { "or": [
        { "field": "ResourceId", "op": "equals", "value": "news-home" },
        { "aspect": "Taggable", "facet": "Tags", "op": "arrayContainsAny", "value": ["featured", "pinned"] }
      ] }
    ]
  },
  "sort": [{ "field": "Views", "aspect": "Content", "descending": true }],
  "skip": 0,
  "take": 20
}
```

- **Filter nodes:** each node sets exactly one of these shapes:
  - `and`, `or` or `not`
  - `exists` (aspect presence)
  - `field` + `op` + `value` (a metadata field)
  - `aspect` + `facet` + `op` + `value` (a facet value)
  - `aspect` + `facet` + `references` (a reference facet naming a resource: a `reference` facet equal to it, or a `reference[]` facet containing it), as `ReferenceQuery.Targets` builds:

    ```json
    { "aspect": "Links", "facet": "Author", "references": "author-1" }
    ```
- **`op`:** names a `ComparisonOperator`, case-insensitively.
- **`scope`:** `latest` (default), `allVersions`, `active` (requires `channel`) or `draft`.
- **`lifecycle`:** `active` (default), `archived`, `softDeleted` or `all`. List and query endpoints therefore exclude archived and soft-deleted resources unless asked; `ResourceQuery` itself stays explicit.
- **Validation:** the endpoint checks the query against the wire format, then against the active provider's capabilities with `IResourceQueryValidator`, before running it.
  - A wire-format error returns `400` with that one diagnostic.
  - Otherwise every validator failure is returned at once, so a client can mark each offending node.
  - Each diagnostic has a `code`, a `path` into the request body (such as `filter.and[1].op` or `sort[0].field`) and, for validator failures, the `feature` it concerns (such as `paging` or `metadata field`).
  - `path` is `null` when the failure concerns no member of the body, such as the tenant.
  - `POST /resources/query/validate` returns the same diagnostics with `200` and `isValid`, without running the query.
- **Response:** `{ items, hasMore }`. The endpoint fetches one row beyond the page to compute `hasMore`, and returns no total count, because counting is a second full query on most providers.

## Tenancy and authorization

- **Tenant:** register an `ITenantScopeAccessor` to resolve the tenant from the request: claims, route or headers. Every endpoint uses it. Without one, the default tenant is used. Never trust a client-named tenant without checking it against the caller's identity.
- **Endpoint authorization:** use host policies, either per group through `AuthorizationPolicies` or on the returned route group. Aster ships no RBAC.
- **Resource authorization:** register an `IAsterResourceAuthorizer` for per-resource checks. It receives the operation (`Read`, `Create`, `Update`, `Activate`), the definition, the resource ID and the current version.
  - A denied single-resource request returns `403`.
  - List and query endpoints drop denied items, so a page can hold fewer items than `take`.

## JSON

The endpoints read every request body and write every response with `AsterEndpointOptions.SerializerOptions`, independent of the host's minimal-API JSON options (`ConfigureHttpJsonOptions`). A host can change its own endpoints' JSON without changing Aster's wire format.

- **Defaults:** `JsonSerializerDefaults.Web` (camelCase properties, case-insensitive reads) plus `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. Enums are written as camelCase names:

  ```json
  { "version": 1, "isLatest": false, "lifecycleState": "softDeleted", "maintenanceDisposition": "possibleCandidate" }
  ```

  Requests accept the names and, for compatibility, enum integers.
- **Problem details:** the Aster members (`diagnostics`, `code`) are serialized with the same options. The envelope (`type`, `title`, `status`, `detail`) goes through the host's ProblemDetails pipeline, so `AddProblemDetails` customizations still apply.
- **Replacing them:** assign other options in `MapAster`. `MapAster` makes them read-only. Aster Studio uses kebab-case enum names, matching its own API:

  ```csharp
  app.MapAster("/api/aster", options => options.SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
  {
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
  });
  ```
