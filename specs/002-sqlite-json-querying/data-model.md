# Data Model: SQLite JSON Querying (Phase 2A)

## Existing Persisted Tables

### `resource_versions`

Stores immutable `Resource` version snapshots.

| Column | Purpose |
|---|---|
| `resource_id` | Logical resource identity shared across versions |
| `version` | Ordinal resource version |
| `id` | Version-specific identifier |
| `definition_id` | Resource definition id |
| `definition_version` | Optional definition version at creation time |
| `created` | ISO-8601 timestamp for the version snapshot |
| `owner` | Optional owner metadata |
| `hash` | Optional payload hash |
| `payload` | JSON serialized `Resource` snapshot |

### `activation_states`

Stores channel activation state.

| Column | Purpose |
|---|---|
| `resource_id` | Logical resource identity |
| `channel` | Activation channel |
| `payload` | JSON serialized `ActivationState` |

## Query Inputs

### `ResourceQuery`

Provider query service consumes the existing `ResourceQuery` model:

- `Scope`
- `ActivationChannel`
- `DefinitionId`
- `Filter`
- `Sorts`
- `Skip`
- `Take`

### Supported `FilterExpression` Shapes

- `MetadataFilter`
- `AspectPresenceFilter`
- `FacetValueFilter`
- `LogicalExpression`

## Internal Provider Helpers

### `SqliteQueryBuilder`

Internal mutable query assembly helper:

- selected payload expression
- `FROM`/joins/subqueries
- `WHERE` fragments
- `ORDER BY` fragments
- `LIMIT`/`OFFSET`
- parameters

### `SqliteWhereTranslator`

Translates supported `FilterExpression` nodes into SQL fragments and parameters.

### `SqliteJsonPath`

Creates safe SQLite JSON paths for:

- aspect presence: path to `Resource.Aspects[aspectKey]`
- facet value: path to `Resource.Aspects[aspectKey][facetDefinitionId]`

The helper must not pass unchecked user text directly into SQL.

### `SqliteParameterBag`

Allocates deterministic parameter names and stores values for `SqliteCommand`.

## Validation Rules

- `Active` scope requires a non-empty `ActivationChannel`.
- `Skip` and `Take` must be non-negative when provided.
- `RangeValue` must specify at least one bound.
- Facet sorting is unsupported unless implemented and tested.
- Unsupported metadata fields fail with `UnsupportedQueryFeatureException`.
