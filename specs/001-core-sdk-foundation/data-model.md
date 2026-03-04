# Data Model: Core SDK Foundation

## Definitions

Define the schema and capabilities of resources.

### ResourceDefinition

Metadata about a type of resource.

| Field | Type | Description |
|---|---|---|
| `Id` | `string` | Unique identifier (e.g., "Product"). |
| `Version` | `int` | Version of the definition itself. |
| `AspectDefinitions` | `Dictionary<string, AspectDefinition>` | Aspect attachments. Key = `AspectDefinition.Id` for unnamed; `"{Id}:{Name}"` composite for named (e.g., `"Tag:Categories"`). |
| `IsSingleton` | `bool` | If true, only one instance can exist. |

### AspectDefinition

Defines a reusable piece of data structure.

| Field | Type | Description |
|---|---|---|
| `Id` | `string` | Unique identifier (e.g., "Price"). |
| `RequiresName` | `bool` | If true, attachment must be named (e.g., "ListingPrice", "SalePrice"). |
| `Schema` | `string` | JSON Schema or Type descriptor (reserved/null for Phase 1). |
| `FacetDefinitions` | `List<FacetDefinition>` | Typed sub-fields within this aspect (optional for Phase 1). |

## Instances

Actual data stored in the system.

### Resource

Concept of a resource identity.

| Field | Type | Description |
|---|---|---|
| `Id` | `string` | Unique identifier (GUID or slug). |
| `DefinitionId` | `string` | Points to `ResourceDefinition.Id`. |
| `Created` | `DateTime` | Creation timestamp. |
| `Owner` | `string` | Creator identity (optional). |

### ResourceVersion

Immutable snapshot of a resource state.

| Field | Type | Description |
|---|---|---|
| `ResourceId` | `string` | FK to Resource. |
| `Version` | `int` | Version number (1, 2, 3...). |
| `Created` | `DateTime` | Creation of this specific version. |
| `Aspects` | `Dictionary<string, object>` | Key: Aspect Name/Id, Value: JSON or Dictionary. |
| `Hash` | `string` | Checksum for integrity (optional). |

### ActivationState

Tracks which versions are active in which channels.

| Field | Type | Description |
|---|---|---|
| `ResourceId` | `string` | FK to Resource. |
| `Channel` | `string` | Channel name (e.g., "Published", "Preview"). |
| `ActiveVersions` | `List<int>` | List of active version numbers (supports Multi-Active). |
| `LastUpdated` | `DateTime` | Timestamp of activation change. |

