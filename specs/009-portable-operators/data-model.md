# Data Model: Portable Operator Expansion

## Comparison Operator

New values:

- `NotEquals`: inverse exact match.
- `In`: exact match against any value in a candidate set.
- `StartsWith`: case-insensitive prefix match.
- `Exists`: facet presence match.

## Membership Value Set

Used by `In`.

Validation rules:

- MUST be non-null.
- MUST be enumerable.
- MUST NOT be a string treated as enumerable characters.
- MUST contain at least one candidate value.

## Facet Exists Predicate

Modeled as:

```csharp
new FacetValueFilter(aspectKey, facetIdentifier, true, ComparisonOperator.Exists)
```

The value is ignored by providers.

## Provider Capability Declaration

Providers list the new operators in `SupportedComparisonOperators` for the filter categories they support.
