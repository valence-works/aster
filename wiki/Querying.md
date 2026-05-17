# Querying

Aster provides a portable **query AST** (`ResourceQuery`) that can be translated to any backend. The in-memory evaluator executes it via LINQ; the SQLite JSON provider translates the same AST to parameterized SQLite SQL plus JSON1 expressions.

---

## `ResourceQuery`

```csharp
public sealed record ResourceQuery
{
    public ResourceVersionScope Scope { get; init; } = ResourceVersionScope.Latest;
    public string? ActivationChannel { get; init; }
    public string? DefinitionId { get; init; }   // filter by resource type
    public FilterExpression? Filter { get; init; } // filter expression tree
    public IReadOnlyList<SortExpression> Sorts { get; init; }
    public int? Skip { get; init; }               // pagination
    public int? Take { get; init; }               // pagination
}
```

---

## Filter Expressions

All filters inherit from `FilterExpression`. There are three concrete leaf types and one combinator.

### `MetadataFilter`

Filter by a top-level resource metadata field.

```csharp
new MetadataFilter(
    Field:    "Owner",
    Value:    "alice",
    Operator: ComparisonOperator.Equals
)
```

Supported fields: `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, `Created`.

### `AspectPresenceFilter`

Presence check — does the resource have this aspect attached?

```csharp
new AspectPresenceFilter(AspectKey: "PriceAspect")
```

For named aspects: `new AspectPresenceFilter("TagsAspect:Categories")`.

### `FacetValueFilter`

Filter by a specific facet value within an aspect.

```csharp
new FacetValueFilter(
    AspectKey:          "TitleAspect",
    FacetDefinitionId:  "Title",
    Value:              "Gadget",
    Operator:           ComparisonOperator.Contains
)
```

### `LogicalExpression`

Combine expressions with `And`, `Or`, or `Not`.

```csharp
new LogicalExpression(LogicalOperator.And, [
    new FacetValueFilter("TitleAspect", "Title", "Gadget", ComparisonOperator.Contains),
    new FacetValueFilter("PriceAspect", "Amount", 50m, ComparisonOperator.Equals),
])
```

For `Not`, supply exactly one operand:

```csharp
new LogicalExpression(LogicalOperator.Not, [
    new AspectPresenceFilter("PriceAspect")
])
```

---

## Comparison Operators

| Operator | Description | In-memory support | SQLite JSON support |
|---|---|---|---|
| `Equals` | Exact match (case-insensitive for strings) | Yes | Yes |
| `NotEquals` | Inverse exact match (case-insensitive for strings) | Yes | Yes |
| `In` | Exact match against any value in a non-string enumerable candidate set | Yes | Yes |
| `Contains` | Substring match (strings only) | Yes | Yes |
| `StartsWith` | Prefix match (strings only, case-insensitive) | Yes | Yes |
| `Range` | Min/max bounds via `RangeValue` | Numeric / date | Numeric scalar facets |
| `Exists` | Facet value is present and non-null | Facets | Facets |

`Range` values use `new RangeValue(min, max, includeMin, includeMax)`. A `null` min or max means the range is unbounded on that side.
`In` values must be non-null, non-string enumerables with at least one candidate. `Exists` is represented as a `FacetValueFilter`; providers ignore the value for that operator.

---

## Running a Query

```csharp
var results = await queryService.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    Filter = new FacetValueFilter(
        AspectKey:         "TitleAspect",
        FacetDefinitionId: "Title",
        Value:             "Gadget",
        Operator:          ComparisonOperator.Contains
    ),
    Skip = 0,
    Take = 20,
});

foreach (var resource in results)
{
    Console.WriteLine(resource.ResourceId);
}
```

`QueryAsync` returns the **latest version** of each matching resource by default. Set `Scope` to `AllVersions`, `Active`, or `Draft` to query a different candidate set; `Active` also requires `ActivationChannel`.

---

## Capability Discovery

Providers expose their supported query surface through `IResourceQueryCapabilitiesProvider`:

```csharp
var capabilities = serviceProvider
    .GetRequiredService<IResourceQueryCapabilitiesProvider>()
    .Capabilities;

Console.WriteLine(capabilities.ProviderName);
Console.WriteLine(capabilities.ProviderKey);
Console.WriteLine(capabilities.SupportsFacetSorting);
```

Capabilities describe supported scopes, filter categories, comparison operators, logical operators, metadata fields, sort categories, paging, facet range value shapes, and known exclusions. The in-memory provider currently declares facet sorting and numeric/date-like facet ranges; SQLite JSON declares metadata sorting, facet sorting, and numeric facet ranges.
Capability declarations are matched to the active query provider by explicit `ProviderKey` values such as `in-memory` and `sqlite-json`.

Custom providers can use `AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>()` to register their active query service and matching capability provider together. Validation fails closed with `capabilities-not-declared` when the active provider key has no matching declaration.

Provider maintainers should cover new providers with the shared provider conformance tests in `test/Aster.Tests/Querying/ProviderConformanceTests.cs`.
Those tests explicitly supply provider setup and fixture data, then verify that declared capabilities match validation and execution behavior.
They are intentionally test-only: Aster still does not use provider registries, runtime scanning, query planners, raw SQL contracts, or public `IQueryable<Resource>` APIs.

## Preflight Validation

Use `IResourceQueryValidator` to validate a query before execution:

```csharp
var validation = validator.Validate(query);

if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code} ({failure.Feature}): {failure.Message}");

    return;
}
```

Validation returns a structured `QueryValidationResult` and does not mutate the query. If no capability provider is declared for the active provider, validation fails closed with `capabilities-not-declared`. Execution still rejects unsupported shapes even when validation is skipped.

Recommended flow for user-defined queries:

1. Inspect provider capabilities when deciding what query UI or API options to expose.
2. Validate a `ResourceQuery` before execution and show all `QueryValidationFailure` entries to the caller.
3. Execute only when validation succeeds.
4. Still handle `UnsupportedQueryFeatureException`, because execution is authoritative and may detect provider-specific constraints.

```csharp
try
{
    var results = await queryService.QueryAsync(query);
}
catch (UnsupportedQueryFeatureException ex)
{
    Console.WriteLine($"{ex.Code} ({ex.Feature}): {ex.Message}");
}
```

## Typed Query Helpers

Typed helpers reduce repeated aspect/facet strings while still producing the same portable AST:

```csharp
public sealed record TitleAspect(string Title);
public sealed record PriceAspect(decimal Amount);

var filter = TypedQuery.And(
    TypedQuery.For<TitleAspect>()
        .Facet(x => x.Title)
        .StartsWith("Gadget"),
    TypedQuery.For<TitleAspect>()
        .Facet(x => x.Title)
        .In("Gadget Pro", "Gadget Plus"),
    TypedQuery.For<PriceAspect>()
        .Facet(x => x.Amount)
        .Range(min: 10m, max: 100m));

var sort = TypedQuery.For<PriceAspect>()
    .Facet(x => x.Amount)
    .Descending();
```

By default, the aspect key is the CLR type name and the facet identifier is the selected member name. Override either value per query when targeting named aspects or non-conventional identifiers:

```csharp
var filter = TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
    .Facet(x => x.Amount, facetIdentifier: "sale_amount")
    .Range(min: 10m, max: 100m);
```

The generated `AspectPresenceFilter` or `FacetValueFilter` remains inspectable before validation or execution.

Typed sort helpers generate ordinary `SortExpression` values, and logical helpers generate ordinary `LogicalExpression` values. Manual AST construction remains fully supported.

---

## Compound Query Example

Find all Products with "Pro" in the title **and** a price under $100:

```csharp
var results = await queryService.QueryAsync(new ResourceQuery
{
    DefinitionId = "Product",
    Filter = new LogicalExpression(LogicalOperator.And, [
        new FacetValueFilter("TitleAspect", "Title", "Pro", ComparisonOperator.Contains),
        new FacetValueFilter(
            "PriceAspect",
            "Amount",
            new RangeValue(Min: null, Max: 100),
            ComparisonOperator.Range),
    ]),
    Sorts = [new SortExpression("Created", SortDirection.Descending)],
    Take = 50,
});
```

---

## SQLite JSON Provider

`Aster.Persistence.SqliteJson` registers `SqliteJsonQueryService` as `IResourceQueryService` when `AddAsterSqliteJson()` is called after `AddAsterCore()`.

The provider supports:

- `Latest`, `AllVersions`, `Active`, and `Draft` scopes; `Active` requires `ActivationChannel`.
- `DefinitionId` shortcut filtering.
- metadata filtering and sorting over `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, and `Created`.
- `Skip` and `Take`.
- aspect presence checks.
- metadata/facet `Equals`, `NotEquals`, `In`, string `Contains`, string `StartsWith`, facet `Exists`, numeric facet `Range`, and facet sorting.
- `And`, `Or`, and single-operand `Not`.

Unsupported SQLite query shapes fail with `UnsupportedQueryFeatureException` instead of falling back to in-memory evaluation. The exception exposes stable `Code`, `Feature`, optional `Path`, and a human-readable message. Current intentional exclusions include metadata range filters, unknown metadata fields, empty ranges, negative paging values, and date-like facet ranges.

## Current Limitations

- **No provider capability planner yet** — capabilities and validation describe what a provider can execute; they do not rewrite queries.
- **Provider-specific semantics are explicit** — SQLite facet ranges are numeric-only in this phase.
- **No public `IQueryable<Resource>`** — LINQ may be used inside a provider, but the public contract stays on the portable AST.

---

## Provider-Agnostic Design

The `ResourceQuery` AST was designed early (Phase 1) to avoid painting future persistence backends into a corner. Key design decisions:

- **No `IQueryable<T>`** as the public abstraction — `IQueryable` is fine within a single backend but breaks when crossing provider boundaries.
- **Explicit operator set** — providers advertise what they support via `IResourceQueryCapabilitiesProvider`.
- **Portable semantics** — `NormalizedText` fields for deterministic substring matching across backends; `Text` fields for provider-specific full-text search. (Phase 3+)

---

## Related

- [Concepts & Terminology](Concepts-and-Terminology) — aspect keys, facet naming
- [Typed Aspects & Facets](Typed-Aspects-and-Facets) — reading aspect values from query results
- [Roadmap](Roadmap) — Phase 3 advanced indexing and typed querying
