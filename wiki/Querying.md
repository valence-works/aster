# Querying

Aster provides a portable **query AST** (`ResourceQuery`) that can be translated to any backend. The Phase 1 in-memory evaluator executes it via LINQ.

---

## `ResourceQuery`

```csharp
public sealed record ResourceQuery
{
    public string? DefinitionId { get; init; }   // filter by resource type
    public FilterExpression? Filter { get; init; } // filter expression tree
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

Supported fields: `ResourceId`, `DefinitionId`, `Owner`, `Version`.

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

| Operator | Description | Phase 1 support |
|---|---|---|
| `Equals` | Exact match (case-insensitive for strings) | ✅ |
| `Contains` | Substring match (strings only) | ✅ |
| `Range` | Min/max bounds (numeric / date) via `RangeValue` | ✅ |

`Range` values use `new RangeValue(min, max, includeMin, includeMax)`. A `null` min or max means the range is unbounded on that side.

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

## Phase 1 Limitations

The in-memory evaluator (`InMemoryQueryService`) works on in-process objects via LINQ. Limitations:

- **No provider capability planner yet** — unsupported fields or value shapes fail at execution time.
- **Sorting is intentionally small** — metadata fields and facet values are supported; provider-specific collation/index rules come later.
- **Facet value comparison** — values are resolved through dictionaries, JSON, or POCO serialization; advanced typed/index semantics come later.

These limitations will be addressed in Phase 2 (persistence backends) and Phase 3 (advanced indexing).

---

## Provider-Agnostic Design

The `ResourceQuery` AST was designed early (Phase 1) to avoid painting future persistence backends into a corner. Key design decisions:

- **No `IQueryable<T>`** as the public abstraction — `IQueryable` is fine within a single backend but breaks when crossing provider boundaries.
- **Explicit operator set** — providers advertise what they support via `IQueryCapabilities` (Phase 3).
- **Portable semantics** — `NormalizedText` fields for deterministic substring matching across backends; `Text` fields for provider-specific full-text search. (Phase 3+)

---

## Related

- [Concepts & Terminology](Concepts-and-Terminology) — aspect keys, facet naming
- [Typed Aspects & Facets](Typed-Aspects-and-Facets) — reading aspect values from query results
- [Roadmap](Roadmap) — Phase 3 advanced indexing and typed querying
