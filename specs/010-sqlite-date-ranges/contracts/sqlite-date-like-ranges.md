# Contract: SQLite Date-Like Facet Ranges

## Supported Query Shape

SQLite JSON MUST support:

```csharp
new FacetValueFilter(
    "Schedule",
    "StartsAt",
    new RangeValue(min, max, includeMin, includeMax),
    ComparisonOperator.Range)
```

when `min` and/or `max` are date-like values.

## Accepted Bounds

Bounds MUST be accepted when they are:

- `DateTime`
- `DateTimeOffset`
- strings parseable as accepted date/time values

Bounds MUST be rejected when they are:

- `DateOnly`
- non-date strings
- numbers mixed with date-like bounds
- unsupported objects, arrays, or booleans

## Accepted Stored Facet Values

Stored facet values MUST match only when they are JSON string scalar values parseable as accepted date/time values.

Stored values MUST NOT match when they are missing, null, malformed, non-string, numeric, boolean, object, or array values.

## Capability Contract

`SqliteJsonQueryCapabilitiesProvider` MUST declare `QueryValueShape.DateTime` in `FacetRangeSupport`.

`UnsupportedFeatures` MUST NOT list date-like facet ranges after this capability is implemented.

## Execution Contract

SQLite translation MUST:

- Normalize query bounds to a comparable round-trip representation.
- Normalize stored date-like strings before comparison.
- Honor inclusive and exclusive bounds.
- Preserve existing numeric range semantics.
- Use parameterized SQL for all query bound values.
