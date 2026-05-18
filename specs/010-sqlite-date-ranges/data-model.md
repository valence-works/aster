# Data Model: SQLite Date-Like Facet Ranges

## Date-Like Facet Range

A facet range filter with:

- `FilterExpression`: `FacetValueFilter`
- `Operator`: `ComparisonOperator.Range`
- `Value`: `RangeValue`
- `RangeValue.Min` and/or `RangeValue.Max`: `DateTime`, `DateTimeOffset`, or accepted date/time string

Validation rules:

- At least one bound MUST be present.
- Bounds MUST resolve to supported query value shapes.
- Mixed numeric/date-like bounds SHOULD fail closed because they are not one coherent range type.

## Accepted Stored Date Value

A JSON scalar string stored inside an aspect payload.

Accepted values:

- Date/time strings parseable as `DateTimeOffset` or `DateTime` using invariant, round-trip-compatible parsing.
- Values equivalent to `System.Text.Json` output for `DateTime` or `DateTimeOffset`.

Rejected values:

- Missing facet values.
- JSON null.
- Numbers, booleans, objects, or arrays.
- Malformed strings.
- Date-only values until date-only shape support is explicitly added.

## Provider Capability Declaration

SQLite JSON facet range support includes:

- `QueryValueShape.Numeric`
- `QueryValueShape.DateTime`

Known unsupported features must no longer list date-like facet ranges after implementation.

## Provider Authoring Failure

If a provider declares date-like facet range support, validation and execution MUST agree:

- Supported date-like range queries validate and execute.
- Unsupported value shapes fail validation.
- Provider-specific execution failures use structured `UnsupportedQueryFeatureException`.
