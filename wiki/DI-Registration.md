# DI Registration & Configuration

Aster Core is registered with a single extension method on `IServiceCollection`.

---

## `AddAsterCore()`

```csharp
using Aster.Core.Extensions;

builder.Services.AddAsterCore();
```

### Registered services

All services are registered as **singletons**.

| Interface | Implementation | Notes |
|---|---|---|
| `IIdentityGenerator` | `GuidIdentityGenerator` | Generates `Guid.NewGuid().ToString()` IDs |
| `IResourceDefinitionStore` | `InMemoryResourceDefinitionStore` | Thread-safe in-memory definition registry |
| `IResourceManager` | `DefaultResourceManager` | Create / update / activate / retrieve resources |
| `IResourceVersionWriter` | `InMemoryResourceStore` | Version/activation write primitive |
| `IResourceVersionReader` | `InMemoryResourceStore` | Query/read candidate version primitive |
| `IResourceQueryService` | `InMemoryQueryService` | LINQ-based query evaluator |
| `IResourceQueryCapabilitiesProvider` | `InMemoryQueryCapabilitiesProvider` | Declares in-memory query support |
| `IResourceQueryValidator` | `ResourceQueryValidator` | Preflights `ResourceQuery` against active provider capabilities |
| `ITypedAspectBinder` | `SystemTextJsonAspectBinder` | Aspect POCO ↔ raw storage via `System.Text.Json` |
| `ITypedFacetBinder` | `SystemTextJsonFacetBinder` | Facet value POCO ↔ raw storage via `System.Text.Json` |

The concrete types (`DefaultResourceManager`, `InMemoryResourceManager`, `InMemoryResourceDefinitionStore`, etc.) are also registered as singletons so they can be resolved directly where needed (e.g., in tests).

---

## `AddAsterSqliteJson()`

`Aster.Persistence.SqliteJson` replaces the default in-memory definition, resource version, and query primitives with SQLite JSON-backed implementations.

```csharp
using Aster.Core.Extensions;
using Aster.Persistence.SqliteJson;

builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options =>
{
    options.ConnectionString = "Data Source=aster.db";
});
```

### Provider-backed services

| Interface | SQLite JSON implementation | Notes |
|---|---|---|
| `IResourceDefinitionStore` | `SqliteJsonResourceStore` | Persists definition versions |
| `IResourceVersionWriter` | `SqliteJsonResourceStore` | Persists resource versions and activation state |
| `IResourceVersionReader` | `SqliteJsonResourceStore` | Reads persisted version sets |
| `IResourceQueryService` | `SqliteJsonQueryService` | Translates supported `ResourceQuery` ASTs to SQLite SQL/JSON queries |
| `IResourceQueryCapabilitiesProvider` | `SqliteJsonQueryCapabilitiesProvider` | Declares SQLite JSON query support |

`AddAsterSqliteJson()` should be called after `AddAsterCore()` so the provider registrations become the resolved implementations for the shared interfaces. The shared `IResourceQueryValidator` uses the active provider's capability declaration when preflighting queries.

Query providers and capability declarations are matched with explicit provider keys. The default in-memory provider uses `in-memory`; the SQLite JSON provider uses `sqlite-json`. If a host replaces `IResourceQueryService` without registering a capability declaration with the same key, validation fails closed with a `capabilities-not-declared` failure instead of silently validating against stale defaults.

Custom query providers should implement `IResourceQueryProviderIdentity` and register a matching `IResourceQueryCapabilitiesProvider`. The recommended path is `AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>()`, which registers the query service, provider identity, and capability provider together:

```csharp
public sealed class MyQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    public string ProviderKey => "my-provider";

    public ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        // Execute the portable ResourceQuery AST against this provider.
        throw new NotImplementedException();
    }
}

public sealed class MyQueryCapabilitiesProvider : IResourceQueryCapabilitiesProvider
{
    public QueryCapabilityDescription Capabilities { get; } = new(
        ProviderKey: "my-provider",
        ProviderName: "My Provider",
        /* supported query surface */);
}

services
    .AddAsterCore()
    .AddResourceQueryProvider<MyQueryService, MyQueryCapabilitiesProvider>();
```

The helper keeps provider selection explicit and does not scan assemblies or create a provider registry. It registers both concrete types and the shared `IResourceQueryService`, `IResourceQueryProviderIdentity`, and `IResourceQueryCapabilitiesProvider` interfaces as singletons. The active `IResourceQueryService` and `IResourceQueryProviderIdentity` resolve to `MyQueryService`; `IResourceQueryCapabilitiesProvider` resolves to `MyQueryCapabilitiesProvider` by normal last-registration-wins DI behavior.

Manual registration remains supported for advanced hosts, including hosts that need non-singleton lifetimes:

```csharp
services.AddSingleton<MyQueryService>();
services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<MyQueryService>());
services.AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<MyQueryService>());
services.AddSingleton<MyQueryCapabilitiesProvider>();
services.AddSingleton<IResourceQueryCapabilitiesProvider>(sp => sp.GetRequiredService<MyQueryCapabilitiesProvider>());
```

If validation returns `capabilities-not-declared`, check that the active query service implements `IResourceQueryProviderIdentity`, exposes a non-empty `ProviderKey`, and has a registered capability declaration with the exact same `ProviderKey`.

---

## Customising the Identity Generator

Implement `IIdentityGenerator` and register before calling `AddAsterCore()`, or replace after:

```csharp
services.AddSingleton<IIdentityGenerator, MySequentialIdGenerator>();
services.AddAsterCore(); // will use your registration if already present, or replace
```

> Tip: `AddAsterCore` uses explicit `AddSingleton<TInterface>(sp => sp.GetRequiredService<TConcrete>())` registrations, so replacing one concrete type replaces the interface resolution.

---

## Customising Typed Binders

Register your own binder before or after `AddAsterCore()`:

```csharp
// Replace the aspect binder with a Newtonsoft.Json implementation
services.AddSingleton<ITypedAspectBinder, NewtonsoftAspectBinder>();
```

---

## Injecting Services

Inject interfaces, not concrete types, in application code:

```csharp
public class ProductService(
    IResourceDefinitionStore definitionStore,
    IResourceManager manager,
    ITypedAspectBinder binder)
{
    // ...
}
```

Inject concrete types only in tests or infrastructure code where you need implementation-specific behaviour.

---

## ASP.NET Core Minimal API (Workbench)

The `Aster.Web` workbench wires everything together:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAsterCore();
builder.Services.AddHostedService<SeedDataInitializer>();

var app = builder.Build();

app.UseStaticFiles();
app.MapDefinitionsEndpoints();
app.MapResourcesEndpoints();

app.Run();
```

Endpoints registered:

| Endpoint | Description |
|---|---|
| `GET /api/definitions` | All registered definitions (latest version per ID) |
| `GET /api/resources/{definitionId}` | All resource versions for the given definition |

---

## Generic Host (non-ASP.NET)

Aster Core does not depend on ASP.NET Core. It works with any `IServiceCollection`-based host:

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddAsterCore();
    })
    .Build();
```

---

## Future Package Layout

As Aster grows, the single `Aster.Core` package will be split per the planned layout:

```
Aster.Abstractions        ← interfaces only (no implementations)
Aster.Definitions         ← builder API
Aster.Runtime             ← services, pipelines, versioning/state
Aster.Querying            ← query model + query service
Aster.Indexing            ← index abstractions + engine
Aster.Persistence.<X>     ← e.g., PostgresJsonb / SqliteJson / Mongo
Aster.Hosting             ← DI extension methods
Aster.Recipes             ← optional recipe execution (separate package)
```

Hosting extension methods will move to `Aster.Hosting` so consumers can depend on only what they need.

---

## Related

- [Getting Started](Getting-Started) — full usage walkthrough
- [Typed Aspects & Facets](Typed-Aspects-and-Facets) — binder usage details
