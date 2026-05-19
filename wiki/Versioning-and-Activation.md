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

var upgrade = await schemaVersions.UpgradeAsync(resource.ResourceId, new ResourceSchemaUpgradeRequest
{
    BaseVersion = latest!.Version,
    TargetDefinitionVersion = status.LatestDefinitionVersion,
    AspectUpdates = new()
    {
        ["SearchAspect"] = new SearchAspect("search text"),
    },
});
```

`UpgradeAsync` appends a new immutable resource version with the requested target definition version. If `TargetDefinitionVersion` is omitted, it defaults to the latest definition version. If the target matches the source lineage, the result is `ResourceSchemaUpgradeStatus.NoOp` and no new version is appended.

Existing aspect data is preserved by default. Aspect keys not declared by the target definition are still carried forward and listed in `CarriedForwardAspectKeys`, so hosts can warn, transform, or clean up explicitly.

Upgrade failures use existing lifecycle exceptions where appropriate: stale `BaseVersion` throws `ConcurrencyException`, and a missing latest resource throws `VersionNotFoundException`. Invalid schema targets throw `ResourceSchemaUpgradeException` with stable codes including `missing-definition`, `missing-definition-version`, `target-definition-version-too-new`, and `target-definition-version-before-source`.

---

## Concurrency Notes

The in-memory store uses concurrent-safe collections and atomic operations internally. For Phase 1 (in-memory only), the optimistic lock check is sufficient.

Phase 6 will harden this for distributed/multi-node scenarios with proper conflict resolution strategies.

---

## Related

- [Getting Started](Getting-Started) — `CreateAsync`, `UpdateAsync`, `ActivateAsync` usage
- [Exception Reference](Exception-Reference) — `ConcurrencyException`, `VersionNotFoundException`
