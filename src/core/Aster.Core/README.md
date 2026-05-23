# Aster.Core

The core SDK for the Aster versioned resource platform.  
Provides **definitions**, **versioned resources**, **activation channels**, **typed aspects**, provider-backed lifecycle orchestration, and default in-memory querying — all wired with standard `Microsoft.Extensions.DependencyInjection`.

---

## Installation

```bash
dotnet add package Aster.Core
```

Or add a project reference:

```xml
<ProjectReference Include="../core/Aster.Core/Aster.Core.csproj" />
```

---

## Quick-start

> Full annotated example: [`specs/001-core-sdk-foundation/quickstart.md`](../../../specs/001-core-sdk-foundation/quickstart.md)

### 1. Register services

```csharp
// Program.cs or wherever you configure DI
builder.Services.AddAsterCore();
```

`AddAsterCore()` registers the core singleton services:

| Service | Interface |
|---|---|
| `InMemoryResourceDefinitionStore` | `IResourceDefinitionStore` |
| `InMemoryResourceStore` | `IResourceVersionReader`, `IResourceVersionWriter` |
| `InMemoryPortabilityStore` | `IResourcePortabilityStore` |
| `DefaultResourceManager` | `IResourceManager` |
| `InMemoryQueryService` | `IResourceQueryService` |
| `InMemoryQueryService` | `IResourceQueryProviderIdentity` |
| `InMemoryQueryCapabilitiesProvider` | `IResourceQueryCapabilitiesProvider` |
| `ResourceQueryValidator` | `IResourceQueryValidator` |
| `ResourceSchemaVersionService` | `IResourceSchemaVersionService` |
| `ResourcePortabilityService` | `IResourcePortabilityService` |
| `GuidIdentityGenerator` | `IIdentityGenerator` |
| `SystemTextJsonAspectBinder` | `ITypedAspectBinder` |
| `SystemTextJsonFacetBinder` | `ITypedFacetBinder` |

---

### 2. Define a resource type

```csharp
var builder = new ResourceDefinitionBuilder();
var definition = builder.WithDefinitionId("Product")
       .WithAspect<TitleAspect>()
       .WithAspect<PriceAspect>()
       .Build();

var definitionStore = serviceProvider.GetRequiredService<IResourceDefinitionStore>();
await definitionStore.RegisterDefinitionAsync(definition);
```

---

### 3. Create a resource

```csharp
var manager = serviceProvider.GetRequiredService<IResourceManager>();

var resource = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new()
    {
        { "Title", new TitleAspect("Super Gadget") },
        { "Price", new PriceAspect(99.99m, "USD") }
    }
});

// resource.ResourceId  → logical ID (shared across all versions)
// resource.Id          → version-specific GUID
// resource.Version     → 1
```

---

### 4. Update a resource (creates a new version)

```csharp
var latest = await manager.GetLatestVersionAsync(resource.ResourceId);

var v2 = await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
{
    BaseVersion = latest.Version,   // optimistic concurrency token
    AspectUpdates = new()
    {
        { "Title", new TitleAspect("Super Gadget Pro") }
    }
});

// v2.Version     → 2
// v2.ResourceId  → same as resource.ResourceId
// v2.Id          → new GUID
```

---

### 5. Activate a version in a channel

```csharp
await manager.ActivateAsync(resource.ResourceId, v2.Version, "Published");

var active = await manager.GetActiveVersionsAsync(resource.ResourceId, "Published");
// active.Single().Version == 2
```

---

### 6. Query resources

```csharp
var queryService = serviceProvider.GetRequiredService<IResourceQueryService>();
var query = new ResourceQuery
{
    DefinitionId = "Product",
    Filter = new FacetValueFilter("Title", "Title", "Super Gadget Pro", ComparisonOperator.Equals),
    Take = 10,
};

var results = await queryService.QueryAsync(query);
```

Preflight the query against the active provider:

```csharp
var validator = serviceProvider.GetRequiredService<IResourceQueryValidator>();
var validation = validator.Validate(query);

if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code} ({failure.Feature}): {failure.Message}");
}
```

Provider capabilities are matched to the active query provider by explicit provider key. If a custom provider has no matching capability declaration, validation fails closed with `capabilities-not-declared`. Query execution still enforces unsupported shapes and throws `UnsupportedQueryFeatureException` with stable `Code`, `Feature`, optional `Path`, and an actionable message.

Custom query providers can be registered with the provider-authoring helper:

```csharp
services
    .AddAsterCore()
    .AddResourceQueryProvider<MyQueryService, MyQueryCapabilitiesProvider>();
```

The helper registers the provider concrete types and shared query/provider interfaces as singletons, keeping the active query service, provider identity, and capability declaration together without introducing provider discovery or a registry. Hosts that need different lifetimes can still use explicit manual DI registration.

Provider capabilities may also declare explicit index projections:

```csharp
IndexProjections:
[
    IndexProjection.Metadata("resource_id", "ResourceId", IndexFieldType.Keyword),
    IndexProjection.Facet("title", "Title", "Title", IndexFieldType.NormalizedText),
    IndexProjection.Facet("tags", "Taxonomy", "Tags", IndexFieldType.KeywordArray),
]
```

Built-in providers declare no default projections. Custom providers can use `IndexProjectionValidator` to validate declarations and `IndexProjectionEvaluator` to turn a resource version into typed projection values plus structured failures such as `missing-source` and `incompatible-value-shape`. Projection declarations are metadata for provider authors; they do not create physical indexes or add query planning.

Or build common typed aspect filters and sorts without repeating convention-based identifiers:

```csharp
var title = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .StartsWith("Gadget");

var titleSet = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .In("Gadget Pro", "Gadget Plus");

var price = TypedQuery.For<PriceAspect>()
    .Facet(x => x.Amount)
    .Range(max: 100m);

var query = new ResourceQuery
{
    Filter = TypedQuery.And(title, titleSet, price),
    Sorts =
    [
        TypedQuery.For<PriceAspect>()
            .Facet(x => x.Amount)
            .Descending(),
    ],
};
```

---

### 7. Inspect and upgrade definition lineage

`CreateAsync` records the active definition version on the new resource. Normal `UpdateAsync` calls preserve that `DefinitionVersion`; they do not silently move long-lived resources to newer schemas.

```csharp
var schemaVersions = serviceProvider.GetRequiredService<IResourceSchemaVersionService>();
var status = await schemaVersions.GetSchemaStatusAsync(resource);

if (status.Status == ResourceSchemaStatus.OlderThanLatest)
{
    var upgraded = await schemaVersions.UpgradeAsync(resource.ResourceId, new ResourceSchemaUpgradeRequest
    {
        BaseVersion = resource.Version,
        TargetDefinitionVersion = status.LatestDefinitionVersion,
    });
}
```

`UpgradeAsync` appends a new resource version with the requested target definition version. When the target is omitted, it defaults to the latest definition version. Existing aspect data is carried forward unless explicitly replaced through `AspectUpdates`; undeclared carried-forward aspect keys are reported in `CarriedForwardAspectKeys`.

Invalid upgrade targets throw `ResourceSchemaUpgradeException` with a stable `Code` such as `missing-definition`, `missing-definition-version`, `target-definition-version-too-new`, or `target-definition-version-before-source`. Stale base versions keep using `ConcurrencyException`.

---

### 8. Export, preview, and import portable snapshots

`IResourcePortabilityService` exports selected definitions, resources, resource versions, and activation state into an SDK-native `PortableSnapshot`. The service also validates snapshots, previews import plans without writing, and performs all-or-nothing imports.

```csharp
var portability = serviceProvider.GetRequiredService<IResourcePortabilityService>();

var export = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    ScopeMode = PortableExportScopeMode.SelectedResources,
    ResourceIds = ["product-1"],
    ResourceVersionScope = PortableResourceVersionScope.AllVersions,
});

if (export.Snapshot is null)
{
    foreach (var diagnostic in export.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

    return;
}

var preview = await portability.PreviewImportAsync(export.Snapshot);

if (preview.CanImport)
{
    var result = await portability.ImportAsync(export.Snapshot);
    Console.WriteLine(result.Status);
}
```

Import is strict by default. Existing identical content is reused, divergent collisions fail before writing, and explicit `PortableImportCollisionMode.RemapDivergent` produces deterministic replacement identifiers while rewriting imported definition/resource references consistently. Preview and import return planned/actual counts, identity mappings, and diagnostics such as `duplicate-snapshot-identity`, `malformed-snapshot`, `missing-definition-reference`, `missing-resource-reference`, and `divergent-identity-collision`.

`IResourcePortabilityStore` is provider-facing infrastructure. Application code should normally use `IResourcePortabilityService`; providers implement `IResourcePortabilityStore` when they need exact snapshot reads and atomic import writes.

---

### 9. Register lifecycle hooks

Hosts can register explicit lifecycle hooks around resource saves, activation/deactivation, export, preview import, and write import.

```csharp
services
    .AddAsterCore()
    .AddResourceLifecycleHook<AuditLifecycleHook>()
    .AddResourceLifecycleHook<PublishPolicyHook>();
```

Hooks run in registration order. Before hooks can reject an operation before mutation by returning `LifecycleHookOutcome.Reject(...)`; save, activation, and deactivation rejections surface as `LifecycleHookException`, while portability rejections surface as `PortableDiagnostic` entries. After hooks run only after the underlying operation succeeds. If an after hook fails, the failure is visible to the caller, but the SDK does not claim rollback of the already-completed operation.

```csharp
public sealed class AuditLifecycleHook : ResourceLifecycleHook
{
    public override ValueTask OnAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{context.SaveKind}: {context.Resource!.ResourceId} v{context.Resource.Version}");
        return ValueTask.CompletedTask;
    }
}
```

Hook registration is ordinary DI registration. Aster does not scan assemblies, use attributes, introduce a provider registry, or require storage providers to implement hook-specific behavior.

---

## Architecture overview

```
IResourceDefinitionStore      — stores versioned ResourceDefinition schemas
IResourceManager              — provider-backed create / update / activate orchestration
IResourceVersionWriter           — low-level version/activation persistence hook
IResourceVersionReader           — low-level read hook for query candidate version sets
IResourcePortabilityService  — exports, validates, previews, and imports portable snapshots
IResourcePortabilityStore    — provider-facing exact snapshot read and atomic import contract
IResourceQueryService         — portable query service; default is LINQ-based in-memory
IResourceQueryProviderIdentity — exposes the active query provider key
IResourceQueryCapabilitiesProvider — declares active provider query support
IResourceQueryValidator        — preflights ResourceQuery against provider capabilities
IResourceSchemaVersionService — inspects and explicitly upgrades resource definition lineage
IResourceLifecycleHook         — explicit host lifecycle hook contract
IResourceLifecycleHookDispatcher — deterministic hook invocation coordinator
ITypedAspectBinder            — serialise/deserialise full aspects (System.Text.Json)
ITypedFacetBinder             — serialise/deserialise individual facet values
IIdentityGenerator            — pluggable ID strategy (default: Guid)
```

Key invariants:

- `Resource` is **immutable** — every `UpdateAsync` call produces a **new** version snapshot. The original version is never mutated.
- `ResourceId` is the **logical** identifier shared across all versions. Each version has its own `Id` (GUID).
- `DefinitionVersion` records schema lineage for a resource version. Normal updates preserve it; explicit schema upgrades advance it.
- Activation channels are independent: a resource can be active in `"Published"` at V2 and in `"Staging"` at V3 simultaneously.
- Optimistic concurrency is enforced on `UpdateAsync` (must supply `BaseVersion == current latest Version`) and `ActivateAsync` (must supply the current latest version number).

---

## Thread safety

All in-memory implementations are thread-safe:

- `ConcurrentDictionary<K,V>` for key-level bucket operations.
- `lock(list)` guards all `List<T>` mutations (version lists, definition version lists).
- `lock(channelActivations)` guards `HashSet<int>` activation sets.

---

## Extending Aster.Core

Swap out any single service by re-registering after `AddAsterCore()`:

```csharp
services.AddAsterCore();
// Override identity generation
services.AddSingleton<IIdentityGenerator, SequentialIdGenerator>();
```

---

## Targets

`net8.0` · `net9.0` · `net10.0`
