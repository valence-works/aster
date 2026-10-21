# Aster.AspNetCore

Mountable ASP.NET Core minimal-API endpoints for [Aster](https://github.com/valence-works/aster): resource definitions, shared aspects, resources and JSON Schema export, with ProblemDetails errors and OpenAPI metadata.

```csharp
builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options => options.ConnectionString = "Data Source=aster.db");

// Optional host hooks:
builder.Services.AddSingleton<ITenantScopeAccessor, MyTenantAccessor>();      // default: TenantScope.Default
builder.Services.AddSingleton<IAsterResourceAuthorizer, MyResourceAuthorizer>(); // default: allow all

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
| Aspects | `GET /aspects`, `GET /aspects/{id}[?version=]`, `GET /aspects/{id}/versions`, `GET /aspects/{id}/schema`, `POST /aspects` |
| Resources | `GET /resources`, `POST /resources/query`, `GET /resources/{id}[?version=]`, `GET /resources/{id}/versions`, `POST /resources`, `PUT /resources/{id}`, `POST /resources/{id}/activate`, `POST /resources/{id}/deactivate` |

- **Errors:** validation failures return `400` ProblemDetails with `diagnostics: [{ code, aspectKey, facetId, message, elementIndex }]`; concurrency and uniqueness conflicts return `409` with a `code`; missing entities return `404`.
- **Lifecycle:** list and query endpoints exclude archived and soft-deleted resources unless `lifecycle=all`, `archived` or `softDeleted`.

See the [wiki page](https://github.com/valence-works/aster/blob/main/wiki/AspNetCore-Endpoints.md) for the query format, authorization and tenancy.
