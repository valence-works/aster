# Quickstart: Query Capabilities & Typed Query Helpers

## Discover Provider Capabilities

```csharp
var capabilities = serviceProvider
    .GetRequiredService<IResourceQueryCapabilitiesProvider>()
    .Capabilities;

Console.WriteLine(capabilities.ProviderName);

foreach (var unsupported in capabilities.UnsupportedFeatures)
    Console.WriteLine(unsupported);
```

## Validate Before Execution

```csharp
var query = new ResourceQuery
{
    DefinitionId = "Product",
    Filter = new FacetValueFilter(
        "TitleAspect",
        "Title",
        "Gadget",
        ComparisonOperator.Contains),
    Sorts = [new SortExpression("Created", SortDirection.Descending)],
    Take = 20,
};

var validator = serviceProvider.GetRequiredService<IResourceQueryValidator>();
var validation = validator.Validate(query);

if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code}: {failure.Message}");

    return;
}

var queryService = serviceProvider.GetRequiredService<IResourceQueryService>();
var results = await queryService.QueryAsync(query);
```

## Build Query Filters With Typed Helpers

```csharp
public sealed record TitleAspect(string Title);
public sealed record PriceAspect(decimal Amount, string Currency);

var filter = new LogicalExpression(LogicalOperator.And, [
    TypedQuery.For<TitleAspect>()
        .Facet(x => x.Title)
        .Contains("Gadget"),
    TypedQuery.For<PriceAspect>()
        .Facet(x => x.Amount)
        .Range(min: 10m, max: 100m),
]);

var query = new ResourceQuery
{
    DefinitionId = "Product",
    Filter = filter,
};
```

## Override Convention Per Query

By default, typed helpers use:

- aspect key = typed aspect CLR type name
- facet identifier = selected member name

Use an explicit override for named aspects or non-conventional identifiers:

```csharp
var salePriceFilter = TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
    .Facet(x => x.Amount, facetIdentifier: "Amount")
    .Range(min: 10m, max: 100m);
```

The generated output is still a normal `FacetValueFilter` and can be inspected or validated before execution.

## Provider Differences

SQLite JSON supports metadata sorting but not facet sorting:

```csharp
var unsupportedForSqlite = new ResourceQuery
{
    Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
};

var validation = validator.Validate(unsupportedForSqlite);
// validation.IsValid == false
// failure message identifies facet sorting as unsupported by SQLite JSON
```
