# Data Model: Core SDK Foundation

## Definitions

Define the schema and capabilities of resources.

> **Universal Versioning Pattern**: All definition and instance models carry both a *logical identifier* (persistent across versions) and a *version-specific unique identifier* (`Id`). Logical IDs are stable keys for lookups and cross-references; `Id` uniquely identifies a single snapshot. Composite key for all versioned models: `(LogicalId, Version)`.

### ResourceDefinition

Metadata about a type of resource.

| Field | Type | Description |
|---|---|---|
| `DefinitionId` | `string` | Logical persistent identifier (e.g., "Product"). Shared across all definition versions. |
| `Id` | `string` | Version-specific unique identifier (GUID). Uniquely identifies this exact definition version. |
| `Version` | `int` | Immutable version number, auto-incremented by the store on each `RegisterDefinitionAsync` call. Starts at 1. Composite key: `(DefinitionId, Version)`. |
| `AspectDefinitions` | `Dictionary<string, AspectDefinition>` | Aspect attachments. Key = `AspectDefinition.AspectDefinitionId` for unnamed; `"{AspectDefinitionId}:{Name}"` composite for named (e.g., `"Tag:Categories"`). |
| `IsSingleton` | `bool` | If true, only one instance can exist. Enforced at `CreateAsync`: throws `SingletonViolationException` if an instance already exists for this definition. |

> **Definition versions are immutable.** `RegisterDefinitionAsync` appends a new version; existing versions are never mutated.

> **Embedding rule (Phase 1)**: `AspectDefinition` and `FacetDefinition` records are **embedded snapshots** within a `ResourceDefinition` version. They are not stored independently; each `ResourceDefinition` version carries its own frozen copies. The `AspectDefinitionId` + `Version` fields exist for traceability and future independent-store upgrade, but no `IAspectDefinitionStore` is required in Phase 1. `FacetDefinition` is a simple field descriptor (no independent versioning) — it inherits its version context from its parent `AspectDefinition`.

### AspectDefinition

Defines a reusable piece of data structure.

| Field | Type | Description |
|---|---|---|
| `AspectDefinitionId` | `string` | Logical persistent identifier (e.g., "Price"). Shared across all aspect definition versions. |
| `Id` | `string` | Version-specific unique identifier (GUID). Uniquely identifies this exact aspect definition version. |
| `Version` | `int` | Immutable version number. Composite key: `(AspectDefinitionId, Version)`. |
| `RequiresName` | `bool` | If true, attachment must be named (e.g., "ListingPrice", "SalePrice"). |
| `Schema` | `string` | JSON Schema or Type descriptor (reserved/null for Phase 1). |
| `FacetDefinitions` | `List<FacetDefinition>` | Typed sub-fields within this aspect. |

### FacetDefinition

Defines a single typed sub-field ("field") within an Aspect (e.g., `Amount` inside `PriceAspect`). Analogous to a Field on a Part in Orchard Core.

| Field | Type | Description |
|---|---|---|
| `FacetDefinitionId` | `string` | Logical identifier for this field within its parent aspect (e.g., "Amount"). Unique within the owning `AspectDefinition`. |
| `Type` | `string` | Data type descriptor ("string", "int", "decimal", "bool", "datetime"). |
| `IsRequired` | `bool` | Whether the field must be present on save. |

## Instances

Actual data stored in the system.

### Resource

A single immutable version snapshot of a resource. `Resource` replaces the former separate *identity* (`Resource`) and *snapshot* (`ResourceVersion`) models — it follows the same universal versioning pattern as definition models.

| Field | Type | Description |
|---|---|---|
| `ResourceId` | `string` | Logical persistent identifier. Shared across all versions. Assigned at V1 by `IIdentityGenerator` or caller. |
| `Id` | `string` | Version-specific unique identifier (GUID). Uniquely identifies this exact resource version. |
| `DefinitionId` | `string` | Logical definition identifier (`ResourceDefinition.DefinitionId`). |
| `DefinitionVersion` | `int?` | Definition version active at creation time (optional, for traceability). |
| `Version` | `int` | Version number (1, 2, 3...). |
| `Created` | `DateTime` | Creation timestamp for this specific version. |
| `Owner` | `string?` | Creator identity; set on V1, carried forward on subsequent versions. |
| `Aspects` | `Dictionary<string, object>` | Key: `AspectDefinitionId` (unnamed) or `"{AspectDefinitionId}:{Name}"` (named). Value: serialized facet data. |
| `Hash` | `string?` | Checksum for integrity (optional). |

> **Status is derived, not stored.** A `Resource` version absent from all `ActivationState.ActiveVersions` entries is implicitly *draft*. Presence in a channel's `ActiveVersions` makes it *active* in that channel.

### ActivationState

Tracks which resource versions are active in which channels.

| Field | Type | Description |
|---|---|---|
| `ResourceId` | `string` | FK to `Resource.ResourceId` (logical). |
| `Channel` | `string` | Channel name (e.g., "Published", "Preview"). |
| `ActiveVersions` | `List<int>` | Active `Resource.Version` numbers (supports Multi-Active). |
| `LastUpdated` | `DateTime` | Timestamp of activation change. |

### AspectInstance

The stored/typed view of one aspect attachment on a `Resource` version (typed companion to the raw `Aspects` dictionary entry).

| Field | Type | Description |
|---|---|---|
| `AspectDefinitionId` | `string` | FK to `AspectDefinition.AspectDefinitionId` (logical). |
| `Name` | `string?` | Discriminator for named attachments; `null` for unnamed. |
| `Facets` | `Dictionary<string, object>` | Facet values keyed by `FacetDefinition.FacetDefinitionId`. |

> **Typed Aspects & Typed Facets**: Both the `AspectInstance` as a whole and individual `Facets` entries support POCO binding. `GetAspect<T>` / `SetAspect<T>` target the full aspect; `GetFacet<T>` / `SetFacet<T>` target a single facet field. See §3.4.

### FacetValue

A single resolved primitive value for one facet; used in Query AST `FacetValue` filter expressions.

| Field | Type | Description |
|---|---|---|
| `FacetDefinitionId` | `string` | FK to `FacetDefinition.FacetDefinitionId` (logical). |
| `Value` | `object` | Raw value (string, int, bool, decimal, DateTime). |

