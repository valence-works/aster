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
    mode: ChannelMode.SingleActive   // required on first activation, stored durably
);
```

### SingleActive vs MultiActive (ChannelMode)

| `ChannelMode` | Behaviour |
|---|---|
| `SingleActive` | All other versions in `channel` are deactivated first. Only V2 is active. |
| `MultiActive` | V2 is added alongside any existing active versions in `channel`. |

The mode is set on **first activation** of each channel and stored durably per `(ResourceId, Channel)` pair. Subsequent activations reuse the stored mode unless an explicit override is supplied:

```csharp
// Second activation — mode already stored as SingleActive, no need to supply again
await manager.ActivateAsync(resource.ResourceId, 3, "Published");
```

> **Migration from `bool allowMultipleActive`:** The boolean parameter has been replaced by `ChannelMode? mode`. Pass `ChannelMode.SingleActive` (equivalent to `false`) or `ChannelMode.MultiActive` (equivalent to `true`).

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

Retrieval:

```csharp
// Latest definition version (default)
var def = await definitionStore.GetDefinitionAsync("Product");

// Specific definition version
var v1def = await definitionStore.GetDefinitionVersionAsync("Product", version: 1);

// All definitions (latest version per DefinitionId)
var all = await definitionStore.ListDefinitionsAsync();
```

---

## Concurrency Notes

The in-memory store uses concurrent-safe collections and atomic operations internally. For Phase 1 (in-memory only), the optimistic lock check is sufficient.

Phase 6 will harden this for distributed/multi-node scenarios with proper conflict resolution strategies.

---

## Related

- [Getting Started](Getting-Started) — `CreateAsync`, `UpdateAsync`, `ActivateAsync` usage
- [Exception Reference](Exception-Reference) — `ConcurrencyException`, `VersionNotFoundException`

