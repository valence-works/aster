# Contract: Portable Query Operators

## Public Comparison Operators

The portable query model MUST expose these additional comparison operators:

- `NotEquals`: matches when the compared value is not equal under provider text/value semantics.
- `In`: matches when the compared value equals any candidate in a non-empty candidate set.
- `StartsWith`: matches when the compared text starts with the supplied prefix using existing case-insensitive text semantics.
- `Exists`: matches when a facet value is present and non-null. Providers SHOULD support this for `FacetValueFilter`; metadata support is not required.

## Validation Contract

Providers MUST declare supported operators in `ResourceQueryCapabilities` for metadata and facet filters independently.

Validation MUST:

- Reject operators that the active provider does not declare for the filter category.
- Reject `In` values that are null, non-enumerable, strings, or empty enumerables.
- Preserve existing structured validation failures for unsupported comparison operators.
- Treat `Exists` as facet-oriented support unless a provider explicitly declares otherwise.

## Execution Contract

Providers that declare support for an operator MUST execute it consistently with validation:

- `NotEquals` MUST use the inverse of equality semantics.
- `In` MUST use equality semantics for every candidate value.
- `StartsWith` MUST use case-insensitive prefix semantics for text values.
- `Exists` MUST match only when the named facet value is present and non-null.

Providers MUST continue to throw structured unsupported query failures when execution encounters a shape it cannot execute.

## Typed Helper Contract

Typed facet helpers SHOULD provide:

```csharp
facet.NotEqualTo(value)
facet.In(values)
facet.StartsWith(prefix)
facet.Exists()
```

The helpers MUST produce the same AST shape as manual `FacetValueFilter` construction with resolved aspect keys and facet identifiers.
