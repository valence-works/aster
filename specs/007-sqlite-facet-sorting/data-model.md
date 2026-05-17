# Data Model: SQLite JSON Facet Sorting

## Facet Sort Expression

- `AspectKey`: Aspect payload key to inspect.
- `Field`: Facet definition id to sort by.
- `Direction`: Ascending or descending.

## SQLite Facet Value Expression

- `Value`: SQL expression that resolves the first matching facet value.
- `IsNumeric`: SQL predicate that identifies integer or real JSON values.

## Capability Declaration

- `SupportsFacetSorting`: Declares whether SQLite JSON accepts facet sort expressions.
- `UnsupportedFeatures`: No longer lists facet sorting once execution support exists.
