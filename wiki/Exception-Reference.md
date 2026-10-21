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
| `InvalidIdentifierException` | `Aster.Core.Exceptions` | A new record's resource ID, definition ID, aspect definition ID, channel or tenant ID starts or ends with whitespace |
| `DuplicateAspectAttachmentException` | `Aster.Core.Exceptions` | Same aspect key attached twice to a definition |
| `ResourceSchemaUpgradeException` | `Aster.Core.Exceptions` | `ThrowIfRejected()` on a rejected schema upgrade result |
| `UnsupportedQueryFeatureException` | `Aster.Core.Exceptions` | Query execution receives a provider-unsupported scope, predicate, sort, paging value, or value shape |

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

## `InvalidIdentifierException`

**Thrown by:** `TenantScope.FromTenantId`, `CreateAsync` (definition ID, caller-supplied `ResourceId`, tenant ID), `RegisterDefinitionAsync` / `RegisterDefinitionIfLatestAsync` and `ResourceDefinitionBuilder.WithDefinitionId` (definition ID, tenant ID), `IAspectDefinitionStore.RegisterAsync` / `RegisterIfLatestAsync` (aspect definition ID, tenant ID), `ActivateAsync` (channel).

**Why:** The identifier starts or ends with whitespace (`char.IsWhiteSpace`: spaces, tabs, newlines, non-breaking spaces). Providers disagree on what such IDs mean (SQL Server treats `"x"` and `"x "` as one key, SQLite and PostgreSQL as two), so core rejects them before anything is written. It derives from `ArgumentException`, like the rejection of a blank ID, and carries:

- `Code`: always `identifier-whitespace` (`InvalidIdentifierException.SurroundingWhitespaceCode`)
- `Identifier`: the rejected value
- `ParamName`: the argument or property that carried it

```csharp
try
{
    await manager.CreateAsync("Product", new CreateResourceRequest { ResourceId = userInput });
}
catch (InvalidIdentifierException exception)
{
    // exception.Code == "identifier-whitespace"; exception.Identifier == userInput
}
```

Snapshot validation and import report the same code as a `PortableDiagnostic` (`PortableDiagnosticCodes.IdentifierWhitespace`) instead of throwing. The ASP.NET Core endpoints return `400` with the code in `diagnostics`.

**Not thrown by** lookups of existing records: reads, `UpdateAsync`, `DeactivateAsync`, history, lifecycle markers, policy application, export and query filters accept any identifier and match it exactly, so data stored under a padded ID before this rule stays reachable. New records, including new versions of a padded definition, cannot be written under such IDs.

**Prevention:** Trim user input before using it as an identifier, or check it with `IdentifierValidator.HasSurroundingWhitespace`.

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

## `UnsupportedQueryFeatureException`

**Thrown by:** `IResourceQueryService.QueryAsync`

**Why:** The active provider cannot execute the supplied `ResourceQuery`. Providers run shared validation before execution and still keep provider-specific checks during translation/execution.

The exception exposes structured details:

| Property | Meaning |
|---|---|
| `Code` | Stable failure code such as `unsupported-facet-sort` or `capabilities-not-declared` |
| `Feature` | Unsupported feature category such as `sort`, `metadata field`, or `value shape` |
| `Path` | Optional query path such as `Sorts[0]` or `Filter.Value` |
| `Message` | Human-readable actionable explanation |

```csharp
try
{
    var results = await queryService.QueryAsync(query);
}
catch (UnsupportedQueryFeatureException ex)
{
    Console.WriteLine($"{ex.Code} ({ex.Feature}): {ex.Message}");
}
```

Use `IResourceQueryValidator` for non-throwing preflight when handling user-defined queries. Execution remains authoritative, so callers should still handle this exception when validation is skipped or provider-specific checks fail later.

When validation returns `capabilities-not-declared`, the active query provider has no matching capability declaration. For custom providers, register the provider and capabilities together with `AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>()` or ensure manual registrations expose the same non-empty provider key.

---

## `ResourceSchemaUpgradeException`

**Thrown by:** `ResourceSchemaUpgradeResult.ThrowIfRejected()`

**Why:** The schema upgrade was rejected. `IResourceSchemaVersionService.UpgradeAsync` itself does not throw for this: it returns `ResourceSchemaUpgradeStatus.Rejected` with `Diagnostics`. `ThrowIfRejected()` turns that result into this exception for callers that prefer exception flow.

The exception exposes structured details:

| Property | Meaning |
|---|---|
| `Code` | Stable failure code: the first diagnostic's |
| `Diagnostics` | Every rejection diagnostic, with the aspect key, facet ID and element index for facet value codes |
| `DefinitionId` | Definition involved in the failed upgrade, when available |
| `TargetDefinitionVersion` | Target definition version, when it resolved |
| `Message` | Human-readable actionable explanation |

Known codes (`ResourceSchemaUpgradeDiagnosticCodes`):

| Code | Meaning |
|---|---|
| `resource-not-found` | The resource has no versions in the tenant |
| `concurrency-conflict` | The base version is stale, or another writer saved the new version first |
| `missing-definition` | The resource's definition ID has no registered definition |
| `missing-definition-version` | The requested target definition version does not exist |
| `target-definition-version-too-new` | The requested target is newer than the latest registered definition version |
| `target-definition-version-before-source` | The requested target is older than the resource's recorded definition lineage |
| `facet-value-*` | The upgraded payload violates the target definition's facets |

`Aster.AspNetCore` maps it to ProblemDetails: `resource-not-found` is `404`, `concurrency-conflict` is `409`, and anything else is `400` with every diagnostic.

```csharp
var result = await schemaVersions.UpgradeAsync(resourceId, request);
if (!result.Succeeded)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}

// Or, keeping exception flow:
try
{
    (await schemaVersions.UpgradeAsync(resourceId, request)).ThrowIfRejected();
}
catch (ResourceSchemaUpgradeException ex)
{
    Console.WriteLine($"{ex.Code}: {ex.Message}");
}
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
