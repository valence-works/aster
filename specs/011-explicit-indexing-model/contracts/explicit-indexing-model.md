# Contract: Explicit Indexing Model

## Public SDK Behavior

The core SDK exposes a provider-neutral indexing model for capability declaration and projection evaluation.

Providers can declare:

- zero or more index projections;
- a stable projection field name;
- a metadata field or aspect/facet source;
- an index field type;
- scalar or multi-value expectation.

Applications can inspect provider capabilities to see declared projections. Empty projection declarations are valid and mean the provider does not declare an index model through this contract.

## Capability Contract

`QueryCapabilityDescription` exposes an index projection collection.

Required behavior:

- Built-in in-memory provider returns an empty collection.
- Built-in SQLite JSON provider returns an empty collection.
- Custom providers can return explicit projections.
- Duplicate projection field names are rejected or reported by validation helpers.
- Existing query capability properties remain source-compatible where practical.

## Projection Source Contract

Supported source shapes:

- metadata field;
- aspect key plus facet key.

Unsupported source shapes:

- nested facet paths;
- JSONPath expressions;
- provider-specific source strings;
- runtime-discovered projection paths.

## Projection Evaluation Contract

Projection evaluation applies declarations to one resource version and returns one combined result:

- successful typed projection values;
- structured per-projection failures.

Evaluation rules:

- Missing sources produce `missing-source`.
- Incompatible values produce `incompatible-value-shape`.
- Invalid declarations produce `invalid-projection-declaration`.
- Evaluation is strict and does not coerce string values into numeric, boolean, or GUID values.
- `DateTime` projections use existing accepted date/time normalization behavior.
- One failure does not prevent other projections from producing values.

## Non-Goals

This contract does not provide:

- physical index creation;
- SQLite generated columns;
- migrations;
- query planning;
- query rewrites;
- runtime scanning;
- automatic discovery;
- public raw SQL;
- public `IQueryable<Resource>`;
- resource-definition index annotations.

## Compatibility

Existing providers and hosts remain valid when they declare no index projections. Query validation and execution behavior must remain unchanged unless callers explicitly use the new indexing model.
