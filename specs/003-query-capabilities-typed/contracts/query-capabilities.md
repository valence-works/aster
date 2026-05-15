# Contract: Query Capabilities & Typed Query Helpers

This contract describes the public SDK surface expected by the feature. Names are provisional for planning, but behavior is normative.

## Capability Discovery

### Provider Capability Source

Providers expose a query capability description for the active query provider.

```csharp
public interface IResourceQueryCapabilitiesProvider
{
    QueryCapabilityDescription Capabilities { get; }
}
```

### Expected Behavior

- The default in-memory provider exposes capabilities matching in-memory query execution.
- The SQLite JSON provider exposes capabilities matching SQLite JSON query execution.
- If no capability provider is registered for the active query provider, validation fails closed with a capabilities-not-declared failure.

## Query Validation

### Validator

```csharp
public interface IResourceQueryValidator
{
    QueryValidationResult Validate(ResourceQuery query);
}
```

### Result Shape

```csharp
public sealed record QueryValidationResult(
    bool IsValid,
    IReadOnlyList<QueryValidationFailure> Failures);

public sealed record QueryValidationFailure(
    string Code,
    string Message,
    string? Path = null,
    string? Feature = null);
```

### Expected Behavior

- Supported queries return `IsValid == true` and no failures.
- Unsupported queries return `IsValid == false` and all detectable failures.
- Validation does not mutate the supplied query.
- Execution still rejects unsupported shapes even when callers skip validation.

## Typed Query Helpers

Typed helpers construct existing portable query model records.

### Convention

- Default aspect key: typed aspect CLR type name.
- Default facet identifier: selected member name.
- Per-query overrides may replace aspect key and/or facet identifier.

### Expected Helper Forms

```csharp
// Presence
FilterExpression hasTitle = TypedQuery.HasAspect<TitleAspect>();

// Equality / contains / range
FilterExpression titleEquals = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .EqualTo("Gadget");

FilterExpression titleContains = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .Contains("Gadget");

FilterExpression priceRange = TypedQuery.For<PriceAspect>()
    .Facet(x => x.Amount)
    .Range(min: 10m, max: 100m);

// Per-query override for named aspects or non-conventional facet identifiers
FilterExpression namedPrice = TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
    .Facet(x => x.Amount, facetIdentifier: "Amount")
    .Range(min: 10m, max: 100m);
```

### Expected Behavior

- Helpers return `FilterExpression` objects that callers place into `ResourceQuery`.
- Generated aspect keys and facet identifiers remain visible in the produced query records.
- Helpers do not expose or return `IQueryable<Resource>`.
- Invalid member selection fails clearly before execution.
