# Typed Aspects & Facets

Aster stores aspect data internally as a dictionary (`IReadOnlyDictionary<string, object>`), but provides first-class support for **typed C# POCOs** so you never have to work with raw dictionaries.

---

## Typed Aspects

### Defining an aspect type

Any C# class or record works. Property names become the facet keys.

```csharp
record TitleAspect(string Title);
record PriceAspect(decimal Amount, string Currency);
record TagsAspect(IReadOnlyList<string> Tags);
```

### Attaching to a definition

```csharp
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithAspect<PriceAspect>()
    .Build();
```

The aspect key defaults to `typeof(T).Name` — i.e. `"TitleAspect"`, `"PriceAspect"`.

### Reading a typed aspect

Use the `GetAspect<T>` extension on `Resource`:

```csharp
using Aster.Core.Extensions;

var price = resource.GetAspect<PriceAspect>("PriceAspect", binder);
// price?.Amount  → 99.99m
// price?.Currency → "USD"
```

Returns `default(T)` if the aspect key is absent.

### Writing a typed aspect

`SetAspect<T>` returns a **new immutable `Resource` record** with the aspect replaced (State Replace semantics):

```csharp
var updated = resource.SetAspect("PriceAspect", new PriceAspect(129.99m, "EUR"), binder);
// `resource` is unchanged; `updated` has the new price
```

> **State Replace semantics**: the entire aspect value is replaced. The POCO is the exclusive source of truth for that aspect key.

---

## Typed Facets

The same POCO binding pattern works at the **individual facet level** via `AspectInstance`.

### Getting an `AspectInstance`

```csharp
// AspectInstance is accessed from the resource's Aspects dictionary
// after deserializing to AspectInstance if you need facet-level access
```

### Reading a typed facet

```csharp
using Aster.Core.Extensions;

var titleValue = aspectInstance.GetFacet<string>("Title", facetBinder);
```

### Writing a typed facet

```csharp
var updated = aspectInstance.SetFacet("Title", "New Title", facetBinder);
// Returns a new AspectInstance; original is unchanged
```

---

## The `ITypedAspectBinder` Interface

The binder is responsible for serializing/deserializing POCOs to/from raw storage format.

```csharp
public interface ITypedAspectBinder
{
    object? Serialize<T>(T value);
    T? Deserialize<T>(object? raw);
}
```

The default implementation (`SystemTextJsonAspectBinder`) uses `System.Text.Json`. It is registered automatically by `AddAsterCore()`.

### Custom binder

To use a different serializer (e.g., Newtonsoft.Json):

```csharp
services.AddSingleton<ITypedAspectBinder, MyNewtonsoftAspectBinder>();
```

---

## The `ITypedFacetBinder` Interface

```csharp
public interface ITypedFacetBinder
{
    object? Serialize<T>(T value);
    T? Deserialize<T>(object? raw);
}
```

Same pattern as `ITypedAspectBinder`, but scoped to individual facet values. Default implementation: `SystemTextJsonFacetBinder`.

---

## Supported Types (Default Binders)

The `System.Text.Json`-based binders support:

| Type | Notes |
|---|---|
| `string` | ✅ |
| `int`, `long` | ✅ |
| `decimal`, `double`, `float` | ✅ |
| `bool` | ✅ |
| `DateTime`, `DateTimeOffset` | ✅ |
| `Guid` | ✅ |
| Nested records/classes | ✅ |
| `IReadOnlyList<T>`, `List<T>` | ✅ |
| `Dictionary<string, T>` | ✅ |

---

## Aspect Key Naming

| Attachment type | Key format | Example |
|---|---|---|
| Unnamed | `typeof(T).Name` | `"TitleAspect"` |
| Named | `"{typeof(T).Name}:{name}"` | `"TagsAspect:Categories"` |

Use consistent keys when reading and writing. Keys are case-sensitive.

---

## Full Round-Trip Example

```csharp
// Define
record PriceAspect(decimal Amount, string Currency);

var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<PriceAspect>()
    .Build();
await definitionStore.RegisterDefinitionAsync(definition);

// Create
var resource = await manager.CreateAsync("Product", new CreateResourceRequest
{
    InitialAspects = new Dictionary<string, object>
    {
        ["PriceAspect"] = new PriceAspect(99.99m, "USD")
    }
});

// Read back
var latest = await manager.GetLatestVersionAsync(resource.ResourceId);
var price = latest!.GetAspect<PriceAspect>("PriceAspect", binder);
Console.WriteLine($"{price?.Amount} {price?.Currency}"); // 99.99 USD

// Update
var v2 = await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
{
    BaseVersion = latest.Version,
    AspectUpdates = new Dictionary<string, object>
    {
        ["PriceAspect"] = new PriceAspect(129.99m, "USD")
    }
});

var v2Price = v2.GetAspect<PriceAspect>("PriceAspect", binder);
Console.WriteLine($"{v2Price?.Amount} {v2Price?.Currency}"); // 129.99 USD
```

---

## Related

- [Getting Started](Getting-Started) — full lifecycle walkthrough
- [DI Registration](DI-Registration) — registering custom binders
- [Querying](Querying) — filtering by aspect/facet values

