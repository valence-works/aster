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
| `Range` | Min/max bounds via `RangeValue` | Numeric / date | Numeric and date-like scalar facets |
| `Exists` | Facet value is present, non-null, and not an empty array | Facets | Facets |
| `ArrayContains` | Facet array contains the single candidate | Facets | Facets |
| `ArrayContainsAny` | Facet array contains at least one candidate | Facets | Facets (up to 100 candidates) |
| `ArrayContainsAll` | Facet array contains every candidate | Facets | Facets (up to 100 candidates) |

`Range` values use `new RangeValue(min, max, includeMin, includeMax)`. A `null` min or max means the range is unbounded on that side.
SQLite date-like facet ranges match JSON string scalar values in the ISO-8601-style shape emitted by `System.Text.Json` for `DateTime` or `DateTimeOffset`. Date-only strings, malformed strings, numbers, booleans, objects, arrays, nulls, and missing facets do not match date-like ranges.
`In` values must be non-null, non-string enumerables with at least one candidate. `Exists` is represented as a `FacetValueFilter`; providers ignore the value for that operator.
### Array operators

The array operators match facets stored as JSON arrays, such as `string[]` facets. They are order-insensitive and duplicate-insensitive, and they combine with `And`, `Or` and `Not` like any other filter. A facet that is missing, `null`, or not an array never matches; queries do not look up facet definitions (ADR 0002 §9), so an array operator against a scalar facet simply returns nothing.

Element equality follows the candidate's CLR type (`QueryArrayValue`):

| Candidate | Matches elements that are |
|---|---|
| `string`, `Guid` | JSON strings, compared ordinally ignoring case. `Guid` candidates are formatted in the hyphenated `D` form, so GUID case doesn't matter but the stored form does. Elements of a `guid[]` facet are always `D` form, because saves reject other forms; untyped string elements in `N`, `B` or `P` form don't match. |
| numeric types | JSON numbers with the same value (`2`, `2L` and `2.0` are equal) |
| `bool` | JSON `true` or `false` |
| `DateTime`, `DateTimeOffset` | JSON strings in an accepted ISO-8601 shape denoting the same UTC instant |

A string candidate never matches a number element and vice versa: `ArrayContains("3")` does not match `[3]`. `ArrayContains` takes one candidate; `ArrayContainsAny` and `ArrayContainsAll` take a non-empty, non-string enumerable. Invalid shapes fail validation with `array-value-required`, `array-values-required`, `empty-array-values`, `unsupported-array-value-shape` (path `Filter.Value[i]`), or `array-values-limit-exceeded` when the candidates exceed the provider's `QueryCapabilityDescription.MaxArrayQueryElements`. Array operators on metadata filters fail with `unsupported-comparison-operator`.

```csharp
// All workflows tagged "billing".
var billing = new FacetValueFilter("Labels", "Tags", "billing", ComparisonOperator.ArrayContains);

// Agents whose allowed environments include staging or prod.
var deployable = TypedQuery.For<AgentAspect>()
    .Facet(x => x.Environments)
    .ArrayContainsAny("staging", "prod");
```

`MetadataFilter.Value` now accepts `object` so metadata `In` predicates can carry enumerable candidate sets; callers that previously read it as `string` should format or pattern-match the value before using string-specific members.

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

Capabilities describe supported scopes, filter categories, comparison operators, logical operators, metadata fields, sort categories, paging, facet range value shapes, and known exclusions. The in-memory and SQLite JSON providers currently declare facet sorting and numeric/date-like facet ranges.
Capability declarations are matched to the active query provider by explicit `ProviderKey` values such as `in-memory` and `sqlite-json`.
Capabilities can also declare index projections through `IndexProjections`. These are provider-authored mappings from resource metadata or an aspect/facet pair to a typed index field. Built-in providers currently declare an empty projection collection; Aster does not infer indexes from definitions or scan aspect types.

```csharp
public sealed class SearchCapabilitiesProvider : IResourceQueryCapabilitiesProvider
{
    public QueryCapabilityDescription Capabilities { get; } = new(
        ProviderKey: "search",
        ProviderName: "Search",
        /* supported query surface */,
        IndexProjections:
        [
            IndexProjection.Metadata("resource_id", "ResourceId", IndexFieldType.Keyword),
            IndexProjection.Facet("title", "Title", "Title", IndexFieldType.NormalizedText),
            IndexProjection.Facet("tags", "Taxonomy", "Tags", IndexFieldType.KeywordArray),
        ]);
}
```

Provider authors can validate declarations with `IndexProjectionValidator` and evaluate them with `IndexProjectionEvaluator` when maintaining their own provider-side index or search document:

```csharp
var validation = new IndexProjectionValidator().Validate(capabilities.IndexProjections);
var projectionResult = new IndexProjectionEvaluator().Evaluate(resource, capabilities.IndexProjections);

foreach (var value in projectionResult.Values)
    Console.WriteLine($"{value.FieldName}: {value.Value}");

foreach (var failure in projectionResult.Failures)
    Console.WriteLine($"{failure.FieldName}: {failure.Code}");
```

Projection evaluation is strict and fail-soft. Scalar projection types require matching CLR value shapes; numeric, boolean, and GUID-looking strings are not coerced. `DateTime` projections reuse the same accepted date/time normalization as portable date ranges. Missing or null sources produce `missing-source`, incompatible shapes produce `incompatible-value-shape`, and invalid declarations produce `invalid-projection-declaration` or `duplicate-projection-field`.

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

By default, the aspect key is the CLR type name and the facet identifier is the selected member's `[JsonPropertyName]`, or its name when it has none (the same ID `WithAspect<T>()` derives). Override either value per query when targeting named aspects or non-conventional identifiers:

```csharp
var filter = TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
    .Facet(x => x.Amount, facetIdentifier: "sale_amount")
    .Range(min: 10m, max: 100m);
```

For array facets, use `ArrayContains`, `ArrayContainsAny` and `ArrayContainsAll`, for example `.Facet(x => x.Tags).ArrayContainsAny("billing", "ops")`. The string `Contains` helper is a substring match on scalar strings.

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

## Querying References

`reference` and `reference[]` facets store target `ResourceId`s as plain strings, so the ordinary operators query them. The helpers below build those filters for you.

**Forward: resources that reference a target.**

```csharp
// A reference facet equals the target, or a reference[] facet contains it.
var filter = ReferenceQuery.Targets("Steps", "Agents", "agent-a");

// Typed equivalent.
var typed = TypedQuery.For<WorkflowSteps>("Steps").Facet(steps => steps.Agents).References("agent-a");
```

As with other text predicates, providers compare case-insensitively. If two `ResourceId`s differ only in letter case, a forward filter matches both.

**Reverse: everything that references a target.** Use this, for example, to check which workflows use agent X before changing or archiving it.

```csharp
var page = await referenceService.GetReferrersAsync("agent-a", new ResourceReferrerQuery
{
    Scope = ResourceVersionScope.Latest, // or Active with ActivationChannel
    DefinitionId = "Workflow",           // optional: only this referrer definition
    Skip = 0,
    Take = 50,
});

foreach (var referrer in page.Items)
    foreach (var location in referrer.Locations)
        Console.WriteLine($"{referrer.Resource.ResourceId}: {location.AspectKey}/{location.FacetDefinitionId}[{location.ElementIndex}]");
// page.HasMore tells whether another page exists.
```

How `IResourceReferenceService` searches:

- **Which facets:** every reference facet declared by the latest version of each definition in the tenant. A facet whose `TargetDefinitionIds` excludes the target's definition still counts, so references saved in lenient mode are found too.
- **How it queries:** it combines those facets into one portable query (`DefinitionId`, plus `Equals` or `ArrayContains` per facet), ordered by `ResourceId` and paged by the provider.
- **What it returns:** each match is confirmed ordinally, and each referrer lists every aspect, facet and element index that names the target.
- **Tenancy:** results come from the given tenant only.

**Cost.** The SQLite provider evaluates the predicates as JSON path expressions on every row of the searched scope, so a lookup is a table scan. That is fine for moderate data sets. Index projections (#100; the Postgres and SQL Server providers back them with indexes) are the extension point: a provider can serve the same `FacetValueFilter` predicates from an index without changing callers.

**Capabilities.** Providers declare support through `QueryCapabilityDescription.SupportsReverseReferenceLookup`; both built-in providers support it. The service throws `NotSupportedException` when the active provider does not.

---

## SQLite JSON Provider

`Aster.Persistence.SqliteJson` registers `SqliteJsonQueryService` as `IResourceQueryService` when `AddAsterSqliteJson()` is called after `AddAsterCore()`.

The provider supports:

- `Latest`, `AllVersions`, `Active`, and `Draft` scopes; `Active` requires `ActivationChannel`.
- `DefinitionId` shortcut filtering.
- metadata filtering and sorting over `ResourceId`, `Id`, `DefinitionId`, `Owner`, `Version`, and `Created`.
- `Skip` and `Take`.
- aspect presence checks.
- metadata/facet `Equals`, `NotEquals`, `In`, string `Contains`, string `StartsWith`, facet `Exists`, numeric/date-like facet `Range`, facet `ArrayContains`/`ArrayContainsAny`/`ArrayContainsAll` (via `json_each`, at most 100 candidates), and facet sorting.
- `And`, `Or`, and single-operand `Not`.

Unsupported SQLite query shapes fail with `UnsupportedQueryFeatureException` instead of falling back to in-memory evaluation. The exception exposes stable `Code`, `Feature`, optional `Path`, and a human-readable message. Current intentional exclusions include metadata range filters, unknown metadata fields, empty ranges, negative paging values, mixed range bound shapes, and invalid date-like range bound values.

## Current Limitations

- **No provider capability planner yet** — capabilities and validation describe what a provider can execute; they do not rewrite queries.
- **Provider-specific semantics are explicit** — SQLite date-like facet ranges require accepted ISO-8601-style JSON string facet values.
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
