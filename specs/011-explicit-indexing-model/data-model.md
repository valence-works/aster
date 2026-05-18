# Data Model: Explicit Indexing Model

## Index Field Type

Represents the declared value shape of an index projection.

Fields:

- `Keyword`: Exact-match scalar string value.
- `Text`: Full text or human-readable string value. No full-text engine is added in this slice.
- `NormalizedText`: Case/culture-normalized string value for provider-defined indexing.
- `Boolean`: Boolean scalar.
- `Integer`: Integral numeric scalar.
- `Decimal`: Decimal numeric scalar.
- `DateTime`: Date/time scalar using the existing accepted date/time value rules.
- `Guid`: GUID scalar.
- `KeywordArray`: Multi-value exact-match string projection.

Validation rules:

- Field type must be one of the declared enum values.
- `KeywordArray` is the only required multi-value field type in this slice.
- No field type implies a physical index or query planner behavior.

## Index Projection Source

Represents where a projected value is read from a resource version.

Source kinds:

- `Metadata`: A resource metadata field, such as `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, or `Created`.
- `Facet`: An explicit aspect key plus facet key pair.

Validation rules:

- Source kind must be metadata or facet.
- Metadata source requires a non-empty metadata field name.
- Facet source requires a non-empty aspect key and non-empty facet key.
- Nested paths, JSONPath syntax, and provider-specific path strings are invalid.

## Index Projection

Provider-declared mapping from a source to an index field.

Fields:

- `FieldName`: Stable index field name exposed by the provider declaration.
- `Source`: Metadata field or aspect/facet pair.
- `FieldType`: Declared index field type.
- `IsMultiValue`: Whether the projection expects multiple values.

Validation rules:

- `FieldName` must be non-empty.
- Projection field names must be unique within one provider declaration.
- `KeywordArray` projections are multi-value by definition.
- Scalar field types must not receive repeated or array values during evaluation.
- Declarations are provider capabilities only; resource definitions are not modified.

## Index Model Capability

Provider capability section that exposes declared projections.

Fields:

- `IndexProjections`: Zero or more index projection declarations.

Validation rules:

- Built-in in-memory and SQLite JSON providers declare zero default projections in this slice.
- Custom/test providers may declare one or more projections.
- Existing query capability behavior must continue when `IndexProjections` is empty.

## Projection Evaluation Result

Fail-soft result produced by applying declarations to a resource version.

Fields:

- `Values`: Successful projection values, keyed or grouped by projection field name.
- `Failures`: Structured per-projection failures.

Failure data:

- Projection field name.
- Source description.
- Stable failure code.
- Human-readable message.

Expected failure codes:

- `missing-source`: Source value is absent.
- `incompatible-value-shape`: Source exists but does not match the declared field type.
- `invalid-projection-declaration`: Projection declaration itself is invalid.

Validation and evaluation rules:

- Evaluation returns successes and failures together.
- Missing source values are distinct from incompatible values.
- String values are not coerced into numeric, boolean, or GUID values.
- `DateTime` values may normalize according to existing accepted date/time rules.
- One projection failure must not discard other successful projection values.

## State Transitions

No persistent lifecycle or state transition is introduced. Projection declarations are static capability data; evaluation is a read-only operation over a resource version.
