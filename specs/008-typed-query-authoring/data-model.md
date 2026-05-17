# Data Model: Typed Query Authoring Ergonomics

## Typed Facet Selection

Represents a typed aspect member selected through an expression.

- `AspectKey`: Resolved from the aspect type name by convention or an explicit override.
- `FacetIdentifier`: Resolved from the selected member name by convention or an explicit override.
- `ValueType`: The selected member's value type, used only by the typed helper API surface.

Validation rules:

- The selector MUST identify one direct readable member on the typed aspect.
- Method calls, nested members, static members, and computed expressions MUST be rejected.

## Typed Facet Sort

Represents a `SortExpression` produced from a typed facet selection.

- `Field`: The resolved facet identifier.
- `Direction`: Ascending or descending.
- `AspectKey`: The resolved aspect key.

Validation rules:

- The helper MUST emit the same `SortExpression` shape a caller could construct manually.
- Provider support is still checked by existing query validation and execution.

## Logical Composition

Represents a `LogicalExpression` produced from existing filters.

- `Operator`: `And`, `Or`, or `Not`.
- `Operands`: Existing `FilterExpression` values.

Validation rules:

- `And` and `Or` MUST require at least one operand.
- `Not` MUST require exactly one operand.
- Helpers MUST preserve operand order.

## Portable Query AST

The existing public query records remain the integration point:

- `ResourceQuery`
- `FilterExpression`
- `FacetValueFilter`
- `AspectPresenceFilter`
- `LogicalExpression`
- `SortExpression`

No new provider-facing data model is introduced.
