# Exception Reference

Aster throws typed exceptions for all domain-level error conditions. All exceptions live in `Aster.Core.Exceptions`.

---

## Exception Summary

| Exception | Namespace | When thrown |
|---|---|---|
| `ConcurrencyException` | `Aster.Core.Exceptions` | Optimistic concurrency conflict on update or activate |
| `VersionNotFoundException` | `Aster.Core.Exceptions` | Requested resource version does not exist |
| `SingletonViolationException` | `Aster.Core.Exceptions` | Attempt to create a second instance of a singleton definition |
| `DuplicateResourceIdException` | `Aster.Core.Exceptions` | Caller-supplied `ResourceId` already exists |
| `DuplicateAspectAttachmentException` | `Aster.Core.Exceptions` | Same aspect key attached twice to a definition |

---

## `ConcurrencyException`

**Thrown by:** `UpdateAsync`, `ActivateAsync`

**Why:** The `BaseVersion` supplied in the request does not match the store's current latest version for that resource. Another concurrent operation modified the resource between your read and your write.

```csharp
try
{
    var v2 = await manager.UpdateAsync(resourceId, new UpdateResourceRequest
    {
        BaseVersion = staleVersion,
        AspectUpdates = ...
    });
}
catch (ConcurrencyException ex)
{
    // Reload the latest version and retry
    var latest = await manager.GetLatestVersionAsync(resourceId);
    // ... merge and retry
}
```

**Resolution:** Reload the latest version, merge your changes, and retry with the updated `BaseVersion`.

---

## `VersionNotFoundException`

**Thrown by:** `ActivateAsync`

**Why:** The specified `version` number does not exist for the resource.

```csharp
try
{
    await manager.ActivateAsync(resourceId, version: 99, "Published");
}
catch (VersionNotFoundException ex)
{
    // Version 99 doesn't exist — check available versions
    var all = await manager.GetVersionsAsync(resourceId);
}
```

---

## `SingletonViolationException`

**Thrown by:** `CreateAsync`

**Why:** The definition has `IsSingleton = true` and at least one instance already exists.

```csharp
// Definition registered with .WithSingleton()
try
{
    var second = await manager.CreateAsync("SiteConfig", new CreateResourceRequest { ... });
}
catch (SingletonViolationException)
{
    // Only one SiteConfig is allowed — retrieve the existing one instead
    var existing = await manager.GetLatestVersionAsync(existingResourceId);
}
```

---

## `DuplicateResourceIdException`

**Thrown by:** `CreateAsync`

**Why:** The caller supplied a `ResourceId` in `CreateResourceRequest.ResourceId` that is already in use.

```csharp
try
{
    await manager.CreateAsync("Product", new CreateResourceRequest
    {
        ResourceId = "product-001",
        InitialAspects = ...
    });
}
catch (DuplicateResourceIdException)
{
    // "product-001" already exists
}
```

**Prevention:** Either omit `ResourceId` (let the engine generate one) or check existence before creating.

---

## `DuplicateAspectAttachmentException`

**Thrown by:** `ResourceDefinitionBuilder.Build()`

**Why:** The same aspect key was attached to the definition more than once. This is a programming error caught at build time.

```csharp
// ❌ This throws DuplicateAspectAttachmentException
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithAspect<TitleAspect>()  // same key "TitleAspect" twice!
    .Build();
```

```csharp
// ✅ Use named aspects to attach the same type more than once
var definition = new ResourceDefinitionBuilder()
    .WithDefinitionId("Article")
    .WithNamedAspect<TagsAspect>("Categories")
    .WithNamedAspect<TagsAspect>("Badges")
    .Build();
```

---

## Handling Pattern

Because all exceptions are typed, you can handle them selectively:

```csharp
using Aster.Core.Exceptions;

try
{
    await manager.UpdateAsync(resourceId, request);
}
catch (ConcurrencyException)
{
    // retry logic
}
catch (VersionNotFoundException)
{
    // resource doesn't exist
}
```

---

## Related

- [Getting Started](Getting-Started) — error conditions in context
- [Versioning & Activation](Versioning-and-Activation) — concurrency details

