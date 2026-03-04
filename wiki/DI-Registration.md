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
| `IResourceManager` | `InMemoryResourceManager` | Create / update / activate / retrieve resources |
| `IResourceWriteStore` | `InMemoryResourceManager` | Write-only surface (same underlying instance) |
| `IResourceQueryService` | `InMemoryQueryService` | LINQ-based query evaluator |
| `ITypedAspectBinder` | `SystemTextJsonAspectBinder` | Aspect POCO ↔ raw storage via `System.Text.Json` |
| `ITypedFacetBinder` | `SystemTextJsonFacetBinder` | Facet value POCO ↔ raw storage via `System.Text.Json` |

The concrete types (`InMemoryResourceManager`, `InMemoryResourceDefinitionStore`, etc.) are also registered as singletons so they can be resolved directly where needed (e.g., in tests).

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

