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

`WithAspect<T>()` also declares one facet per public property, so the definition carries a schema that is validated on save (ADR 0002 §8):

| Property type | Facet type |
|---|---|
| `string` | `string` |
| `int`, `long`, `short`, `byte` (and nullable forms) | `int` |
| `decimal`, `double`, `float` | `decimal` |
| `bool` | `bool` |
| `DateTime`, `DateTimeOffset` | `datetime` |
| `Guid` | `guid` |
| `T[]`, `List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>`, `ISet<T>` (and similar) of a type above | `T[]`, e.g. `string[]` |
| nested classes, records, structs, and dictionaries | `object` |
| `string`, or a collection of strings, marked `[ReferenceFacet]` | `reference` or `reference[]` (see [Reference facets](#reference-facets)) |

The facet ID is the property's `[JsonPropertyName]` when it has one, otherwise the property name, so it matches the key in stored JSON. Properties declared with the C# `required` modifier become required facets; for a collection, required means at least one element. Enum properties are not derived yet (#131). Also skipped: collections of nullable elements (`List<int?>`) or of nested types (`object[]` is not a facet type), properties typed as `object`, and types System.Text.Json writes as strings, such as `byte[]`, `TimeSpan`, `Uri` and types with a `[JsonConverter]`. Decorate a derived facet with metadata by ID:

```csharp
new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithFacetMetadata("Title", new FacetMetadata { MaxLength = 80 });
```

### Array facets

A facet type ending in `[]` holds a JSON array of one element type: `string[]`, `int[]`, `decimal[]`, `bool[]`, `datetime[]` or `guid[]` (aliases work too, so `int32[]` means `int[]`). Nested arrays and `object[]` are not supported.

On save, each element must match the element type; a `null` element is a type mismatch. Element-level diagnostics carry `ElementIndex`. Value constraints (`AllowedValues`, `MinLength`/`MaxLength`, `Pattern`, `Minimum`/`Maximum`) apply to every element, and three constraints apply to the array as a whole:

| Metadata | Rule | Diagnostic |
|---|---|---|
| `MinItems`, `MaxItems` | element count bounds (non-negative, `MinItems <= MaxItems`) | `facet-value-items-out-of-range` |
| `UniqueItems` | no duplicates after normalization (strings compare ordinally, GUIDs case-insensitively, date-times as UTC instants) | `facet-value-items-not-unique` |

```csharp
new ResourceDefinitionBuilder()
    .WithDefinitionId("Workflow")
    .WithAspect("Labels")
    .WithFacet("Tags", "string[]", isRequired: true)
    .WithFacetMetadata(new FacetMetadata { MaxItems = 10, UniqueItems = true, Pattern = "^[a-z-]+$" });
```

Query array facets with `ArrayContains`, `ArrayContainsAny` and `ArrayContainsAll`; see [Querying](Querying#array-operators).

### Object facets

An `object` facet stores a structured value, such as model settings or connector configuration, as a JSON object, verbatim. On save the value must be a JSON object, a dictionary, or a POCO; anything else is `facet-value-type-mismatch`. Object facets are opaque in 1.0: nested facet definitions, `AllowedValues`, `DefaultValue` and other value constraints do not apply, and only `Exists` is meaningful in queries (queries are definition-agnostic, so other operators simply do not match object values).

### Reference facets

A `reference` facet holds the `ResourceId` of another resource; `reference[]` holds several (with the usual array constraints). On save the value must be a non-blank string. How a reference resolves is declared once per facet:

| Metadata | Meaning |
|---|---|
| `TargetDefinitionIds` | Definition IDs the target may have; undeclared means any. Non-empty, no blank or duplicate entries. |
| `ReferenceResolution` | `null` or `"latest"`: resolve the target's latest version. Any other value names the activation channel whose active version is used. |

```csharp
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Agent")
    .WithAspect("Links")
    .WithFacet("Secret", "reference", isRequired: true)
    .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Secret"], ReferenceResolution = "production" })
    .Build();

var resolution = await referenceResolver.ResolveAsync(definition, "Links", "Secret", "sec-openai", tenant);
// resolution.Status: Resolved, TargetNotFound, TargetTypeMismatch, or NotActiveInChannel
```

`IResourceReferenceResolver` looks targets up in the given tenant only (cross-tenant references are not supported). With channel resolution, if several versions are active (activated with `allowMultipleActive: true`), the newest one wins. References are stored as plain strings, so they can be queried with `Equals`, `In` and the array operators. Per-value version pins are not supported (ADR 0002 §6): store an exact version in a sibling `int` facet when you need one. References are never inferred from typed aspect properties; declare them explicitly, by hand as above or with `[ReferenceFacet]`.

#### References in typed aspects

Mark a `string` property, or a collection that would derive `string[]` (`string[]`, `List<string>`, `IReadOnlyList<string>` and the like), with `[ReferenceFacet]` to derive a `reference` or `reference[]` facet instead. The attribute takes the target definition IDs and an optional `ReferenceResolution`:

```csharp
public sealed record Agent
{
    public required string Name { get; init; }

    [ReferenceFacet("Prompt")]
    public required string Prompt { get; init; }

    [ReferenceFacet("Tool", "Workflow", ReferenceResolution = "production")]
    public string[] Tools { get; init; } = [];
}

new ResourceDefinitionBuilder()
    .WithDefinitionId("Agent")
    .WithAspect<Agent>()
    .WithFacetMetadata(nameof(Agent.Tools), new FacetMetadata { UniqueItems = true });
```

The derived facets are the ones `WithFacet("Prompt", "reference", isRequired: true)` and `WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Prompt"] })` would declare, so registration, [JSON Schema export](Json-Schema-Export) and save-time validation treat them the same. `[ReferenceFacet]` with no target IDs allows any definition. `WithFacetMetadata` on the facet keeps the attribute's `TargetDefinitionIds` and `ReferenceResolution` unless the new metadata declares its own. On any other property type, or one with its own `[JsonConverter]`, `WithAspect<T>()` (and `AspectDefinitionBuilder.FromType<T>()`) throws `InvalidOperationException` naming the property.

#### Save-time reference validation

Creates and updates also check each well-typed reference value against the store, in the saving tenant:

| Code | When |
|---|---|
| `reference-target-missing` | No resource with that `ResourceId` exists. |
| `reference-target-type-mismatch` | The target's definition is not in `TargetDefinitionIds`. |
| `reference-pin-missing` | The facet resolves through a channel, and the target has no version active in it. |

Diagnostics carry the aspect key, facet ID and, for `reference[]`, the element index. The checks cost one bounded latest-version read for every referenced resource, plus one active-version read per declared channel, so there are no per-value reads. An update checks only the aspects it writes: a target that disappears or is deactivated later does not block unrelated edits.

The host chooses what a failing reference does:

```csharp
services.AddAsterCore(options => options.ReferenceValidation = ReferenceValidationMode.Lenient);
```

- `Strict` (the default) rejects the save with a `FacetValueValidationException`; nothing is persisted.
- `Lenient` saves. The diagnostics are logged as warnings and passed to lifecycle hooks as `ResourceSaveLifecycleContext.ReferenceWarnings`.

To show the same diagnostics before saving (for example, in an editor), call `IResourceReferenceValidator.ValidateAsync(definition, aspects, tenant)`. Portability import and schema upgrades skip these checks. Portability import skips facet value validation too; a schema upgrade validates facet values against its target definition and returns the diagnostics as a rejection.

### Untyped aspects

Aspects stored as dictionaries declare their facets explicitly:

```csharp
new ResourceDefinitionBuilder()
    .WithDefinitionId("Agent")
    .WithAspect("Prompt")
    .WithFacet("Template", "string", isRequired: true)
    .WithFacet("MaxTokens", "int");
```

`WithTypedFacet<T>()` is obsolete: it names the facet after its CLR type. Use `WithFacet` or a typed aspect instead.

Facet IDs within one aspect must not differ only in the case of their first letter. Facet values are read by ID or by its camel-case form (`title` for `Title`), so registration refuses `Name` next to `name` with `facet-metadata-id-case-collision`, whether the facets are declared with `WithFacet`, derived from a typed aspect's properties, or declared on a shared aspect definition.

### Shared aspects

Register an aspect once in `IAspectDefinitionStore`, then attach it to any number of resource definitions. This follows ADR 0003.

```csharp
var aspects = services.GetRequiredService<IAspectDefinitionStore>();
var tenant = TenantScope.FromTenantId("acme"); // register and resolve in the same tenant

// Register by hand...
await aspects.RegisterAsync(new AspectDefinition
{
    AspectDefinitionId = "Seo",
    Id = Guid.NewGuid().ToString(),
    Version = 0, // assigned by the store
    FacetDefinitions = [/* ... */],
}, tenant);
// ...or derive it from a POCO; the aspect ID is the type name ("AuditAspect")
await aspects.RegisterAsync(AspectDefinitionBuilder.FromType<AuditAspect>(requiresName: true), tenant);

var article = await new ResourceDefinitionBuilder()
    .WithDefinitionId("Article")
    .WithSharedAspect("Seo")                                     // latest version at build time
    .WithSharedAspect<AuditAspect>(version: 1, name: "Created")  // pinned; named because RequiresName
    .WithSharedAspect<AuditAspect>(version: 1, name: "Updated")
    .BuildAsync(aspects, tenant);
await definitionStore.RegisterDefinitionAsync(article, tenant, ct);
```

**What `BuildAsync` does**
- It copies each pinned (or latest) stored version unchanged into the definition, with `IsShared = true`. The pin is `(AspectDefinitionId, Version)`.
- Validation, rendering and queries keep using that embedded copy. Registering aspect v2 later changes nothing until you move a definition onto it explicitly.

**When it fails**
- It throws `SharedAspectResolutionException`, listing every failure:
  - `aspect-definition-not-found`: the aspect or the pinned version is missing in the tenant.
  - `aspect-name-required`: the stored aspect has `RequiresName` and the attachment is unnamed.
- Synchronous `Build()` throws when shared aspects are attached.
- You can't add facets to a shared attachment, or decorate them; register a new aspect version instead.

`WithAspect<T>()` stays definition-local and never touches the store.

**Upgrading to a new aspect version.** Nothing propagates on its own. Move definitions over explicitly:

```csharp
var upgrades = services.GetRequiredService<IAspectUpgradeService>();
var preview = await upgrades.PreviewAsync("Seo");       // definitions pinning older versions, plus a facet diff
var result = await upgrades.ApplyAsync(new AspectUpgradeRequest
{
    AspectDefinitionId = "Seo",
    DefinitionIds = ["Article"],                          // only these get new definition versions
});
// Resources then move with IResourceSchemaVersionService.UpgradeAsync(..., TargetDefinitionVersion = n).
```

**What the preview reports.** Each facet change is one of `Added`, `Removed`, `TypeChanged`, `RequiredChanged` or `MetadataChanged`.

**What apply returns.** One outcome per selected definition:
- `Upgraded`, with the new definition version.
- `NotApplicable`: the definition has no older pin (`aspect-upgrade-not-applicable`).
- `Failed`, for example:
  - `aspect-name-required`: the target requires a name and the attachment is unnamed.
  - `definition-version-conflict`: another writer registered a version of the definition after the upgrade read it. Nothing is written; preview again and retry.

**Portability:** `IResourcePortabilityService.ExportAsync` includes the pinned aspect versions in the snapshot. Import registers them in the target tenant with their versions preserved, before the definitions that pin them. Pins that resolve nowhere fail with `unresolved-aspect-pin`. Aspect versions and definitions that the target already holds are matched by content, not by the IDs generated when they were registered (#195, #221): an environment that registered the same aspects and definitions from the same code reuses its own copies instead of remapping or failing.

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

