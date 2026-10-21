# Versioning & Activation

Aster uses an **append-only, immutable versioning model** combined with a **channel-based activation system**. These two concepts are intentionally decoupled.

---

## Immutable Versions

Every resource has a stable `ResourceId` (its identity across all versions) and a sequence of version snapshots, each identified by a unique `Id` GUID and an ordinal `Version` number (1, 2, 3, …).

- Calling `CreateAsync` produces **Version 1**.
- Calling `UpdateAsync` always appends a **new version** — the previous version is never modified.
- There is no in-place edit.

### Identity model

| Property | Type | Description |
|---|---|---|
| `ResourceId` | `string` | Logical persistent identifier. Stable across all versions. |
| `Id` | `string` | GUID uniquely identifying this exact version snapshot. |
| `Version` | `int` | Ordinal version number. Auto-incremented. |
| `DefinitionId` | `string` | Logical definition identifier at creation time. |
| `DefinitionVersion` | `int?` | Definition version active at creation time (traceability). |
| `Created` | `DateTime` | UTC timestamp for this specific version. |

---

## Draft vs Active

There is no explicit `Status` field on a resource. Status is **derived**:

| Condition | Derived status |
|---|---|
| No activation entries for this version | **Draft** |
| Present in at least one channel's active set | **Active** |

---

## Optimistic Concurrency

All mutating operations use **optimistic concurrency**. You must supply a `BaseVersion` that matches the store's current latest version:

```csharp
var v2 = await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
{
    BaseVersion = resource.Version, // must equal the current latest
    AspectUpdates = ...
});
```

If the resource was modified between your read and your write, `ConcurrencyException` is thrown. Reload and retry.

The same check applies to `ActivateAsync`.

---

## Channel-Based Activation

A **channel** is a named delivery context. Activation places a specific resource version into a channel.

### Activating a version

```csharp
await manager.ActivateAsync(
    resourceId: resource.ResourceId,
    version: 2,
    channel: "Published",
    allowMultipleActive: false   // default
);
```

### Single-active vs multi-active

| `allowMultipleActive` | Behaviour |
|---|---|
| `false` (default) | All other versions in `channel` are deactivated first. Only V2 is active. |
| `true` | V2 is added alongside any existing active versions in `channel`. |

### Multiple channels

A single resource version can be active in multiple channels simultaneously:

```csharp
await manager.ActivateAsync(resource.ResourceId, 2, "Published");
await manager.ActivateAsync(resource.ResourceId, 2, "Staging");
await manager.ActivateAsync(resource.ResourceId, 1, "Legacy");
```

---

## Retrieval Helpers

| Method | Returns |
|---|---|
| `GetLatestVersionAsync(resourceId)` | The version with the highest `Version` number |
| `GetVersionAsync(resourceId, version)` | A specific version snapshot |
| `GetVersionsAsync(resourceId)` | All versions in order |
| `GetActiveVersionsAsync(resourceId, channel)` | All versions currently active in the named channel |

All return `null` / empty if not found, except `GetVersionAsync` which returns `null`.

---

## Definition Versioning

Resource Definitions follow the same append-only model:

```csharp
// First registration → Version 1
await definitionStore.RegisterDefinitionAsync(definition);

// Update the definition (e.g., add an aspect) → Version 2
var updatedDef = new ResourceDefinitionBuilder()
    .WithDefinitionId("Product")
    .WithAspect<TitleAspect>()
    .WithAspect<PriceAspect>()
    .WithAspect<StockAspect>()   // new
    .Build();
await definitionStore.RegisterDefinitionAsync(updatedDef);
```

### Concurrent edits

`RegisterDefinitionAsync` always appends, so a read-modify-register edit can silently supersede a version another writer registered in between. To prevent this, register conditionally on the version you read:

```csharp
var current = await definitionStore.GetDefinitionAsync("Product", tenant, ct);
var edited = /* build the new definition from current */;

var result = await definitionStore.RegisterDefinitionIfLatestAsync(edited, current!.Version, tenant, ct);
if (!result.IsRegistered)
{
    // Another writer registered result.LatestVersion first; nothing was written. Re-read and retry.
}
```

- The check and the write are atomic on both built-in providers.
- Pass `0` to create a definition only if it doesn't exist yet.
- On success, `result.Definition` is the stored version.

Existing resources continue to reference the definition version they were created against. `DefinitionVersion` on a `Resource` records which definition version was active at creation time.

Normal resource updates preserve the source resource version's `DefinitionVersion`. Registering a new definition version does not automatically rewrite existing resources or move them to the newer schema.

Retrieval:

```csharp
// Latest definition version (default)
var def = await definitionStore.GetDefinitionAsync("Product");

// Specific definition version
var v1def = await definitionStore.GetDefinitionVersionAsync("Product", version: 1);

// All definitions (latest version per DefinitionId)
var all = await definitionStore.ListDefinitionsAsync();
```

## Schema Status and Explicit Upgrades

Use `IResourceSchemaVersionService` to inspect one resource version's schema status relative to registered definition versions:

```csharp
var schemaVersions = serviceProvider.GetRequiredService<IResourceSchemaVersionService>();
var status = await schemaVersions.GetSchemaStatusAsync(resource);
```

Possible `ResourceSchemaStatus` values:

| Status | Meaning |
|---|---|
| `Current` | The resource version references the latest available definition version |
| `OlderThanLatest` | The resource version references an available definition version older than latest |
| `MissingDefinition` | No definition exists for the resource's `DefinitionId` |
| `MissingDefinitionVersion` | The recorded definition version cannot be found, or is newer than the latest known version |
| `UnknownResourceLineage` | The resource version does not record `DefinitionVersion` |

To advance lineage, explicitly request an upgrade against the latest resource version:

```csharp
var latest = await manager.GetLatestVersionAsync(resource.ResourceId);

var request = new ResourceSchemaUpgradeRequest
{
    BaseVersion = latest!.Version,
    TargetDefinitionVersion = status.LatestDefinitionVersion,
    AspectUpdates = new()
    {
        ["SearchAspect"] = new SearchAspect("search text"),
    },
};

var preview = await schemaVersions.UpgradePreviewAsync(resource.ResourceId, request); // writes nothing
var upgrade = await schemaVersions.UpgradeAsync(resource.ResourceId, request);
if (upgrade.Status == ResourceSchemaUpgradeStatus.Rejected)
{
    foreach (var diagnostic in upgrade.Diagnostics)
        Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

`UpgradeAsync` appends a new immutable resource version with the requested target definition version. If `TargetDefinitionVersion` is omitted, it defaults to the latest definition version. If the target matches the source lineage, the result is `ResourceSchemaUpgradeStatus.NoOp` and no new version is appended.

Existing aspect data is preserved by default. Aspect keys not declared by the target definition are still carried forward and listed in `CarriedForwardAspectKeys`, so hosts can warn, transform, or clean up explicitly.

The new version is recorded against the target definition, so its payload (the latest aspects plus `AspectUpdates`) is validated against that definition's facets before it is written. Supply missing or changed values through `AspectUpdates`. Carried-forward aspects the target does not declare are not validated.

`UpgradePreviewAsync` takes the same request and writes nothing. It reports the `Status` the upgrade would return, the target definition version, the carried-forward keys, and the blocking diagnostics, so a host can show them before asking for confirmation.

A request that cannot be applied returns `ResourceSchemaUpgradeStatus.Rejected` with `Diagnostics` and writes nothing:

| Code | When |
|---|---|
| `resource-not-found` | The resource has no versions in the tenant |
| `concurrency-conflict` | `BaseVersion` is not the latest version, or another writer saved the new version first |
| `missing-definition` | The resource's definition is not registered |
| `missing-definition-version` | The target definition version does not exist |
| `target-definition-version-too-new` | The target is newer than the latest registered definition version |
| `target-definition-version-before-source` | The target is older than the resource's recorded definition version |
| `facet-value-*` | The upgraded payload violates the target definition; the diagnostic carries the aspect key, facet ID and element index |

A stale base version is reported together with the other checks, so one preview lists every blocking reason. Exceptions remain for programming errors, lifecycle hook failures and store failures. To keep exception flow, call `upgrade.ThrowIfRejected()`, which throws `ResourceSchemaUpgradeException` with the same diagnostics.

---

## Concurrency Notes

The in-memory store uses concurrent-safe collections and atomic operations internally. For Phase 1 (in-memory only), the optimistic lock check is sufficient.

Phase 6 will harden this for distributed/multi-node scenarios with proper conflict resolution strategies.

---

## Related

- [Getting Started](Getting-Started) — `CreateAsync`, `UpdateAsync`, `ActivateAsync` usage
- [Exception Reference](Exception-Reference) — `ConcurrencyException`, `VersionNotFoundException`
