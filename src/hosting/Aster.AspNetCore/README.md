# Aster.AspNetCore

Mountable ASP.NET Core minimal-API endpoints for [Aster](https://github.com/valence-works/aster): resource definitions, shared aspects, resources and JSON Schema export, with ProblemDetails errors and OpenAPI metadata.

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options => options.ConnectionString = "Data Source=aster.db");

// Optional host hooks:
builder.Services.AddSingleton<ITenantScopeAccessor, MyTenantAccessor>();      // default: TenantScope.Default
builder.Services.AddSingleton<IAsterResourceAuthorizer, MyResourceAuthorizer>(); // default: allow all

// The host owns authorization; register every policy named below.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("aster-admin", policy => policy.RequireRole("aster-admin")));

var app = builder.Build();
app.MapAster("/api/aster", options =>
{
    options.AuthorizationPolicies[AsterEndpointGroups.Definitions] = "aster-admin";
    // options.ReadOnly = true;
    // options.Groups = AsterEndpointGroups.Resources;
}).RequireAuthorization();
```

| Group | Endpoints |
|---|---|
| Definitions | `GET /definitions`, `GET /definitions/{id}[?version=]`, `GET /definitions/{id}/schema`, `POST /definitions[?expectedLatestVersion=]` |
| Aspects | `GET /aspects`, `GET /aspects/{id}[?version=]`, `GET /aspects/{id}/versions`, `GET /aspects/{id}/schema`, `POST /aspects[?expectedLatestVersion=]` |
| Resources | `GET /resources`, `POST /resources/query`, `POST /resources/query/validate`, `GET /resources/query/capabilities`, `GET /resources/{id}[?version=]`, `GET /resources/{id}/versions`, `POST /resources`, `PUT /resources/{id}`, `POST /resources/{id}/activate`, `POST /resources/{id}/deactivate` |

- **Errors:** validation failures return `400` ProblemDetails with `diagnostics: [{ code, aspectKey, facetId, message, elementIndex, path, feature }]`; concurrency and uniqueness conflicts return `409` with a `code`; missing entities return `404`. Queries return every validation failure at once. Endpoints a host maps inside an Aster group get the same mapping, including `ResourceSchemaUpgradeException` from `ThrowIfRejected()` on a rejected schema upgrade (`404`, `409` or `400` by its first code).
- **Lifecycle:** list and query endpoints exclude archived and soft-deleted resources unless `lifecycle=all`, `archived` or `softDeleted`.

## JSON

- **Own options:** the endpoints read request bodies and write responses with `AsterEndpointOptions.SerializerOptions`, not the host's `ConfigureHttpJsonOptions`, so a host's global JSON settings do not change Aster's wire format.
- **Defaults:** web defaults (camelCase properties, case-insensitive reads) with enums as camelCase names, such as `"lifecycleState": "softDeleted"` and `"maintenanceDisposition": "possibleCandidate"`. Enum integers are still accepted in requests.
- **Problems:** the `diagnostics` and `code` members use the same options; the ProblemDetails envelope still goes through the host's ProblemDetails pipeline.
- **Replace them** in `MapAster`, for example for kebab-case enum names as Aster Studio does:

```csharp
app.MapAster("/api/aster", options => options.SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
});
```

See the [wiki page](https://github.com/valence-works/aster/blob/main/wiki/AspNetCore-Endpoints.md) for the query format, authorization and tenancy.
