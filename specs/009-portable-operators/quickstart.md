# Quickstart: Portable Operator Expansion

## Manual Queries

```csharp
var query = new ResourceQuery
{
    DefinitionId = "Product",
    Filter = TypedQuery.And(
        new MetadataFilter("Owner", "archived", ComparisonOperator.NotEquals),
        new FacetValueFilter("Title", "Title", "Pro", ComparisonOperator.StartsWith),
        new FacetValueFilter("Status", "Code", new[] { "Draft", "Ready" }, ComparisonOperator.In),
        new FacetValueFilter("Price", "Amount", true, ComparisonOperator.Exists)),
};
```

## Typed Helpers

```csharp
var filter = TypedQuery.And(
    TypedQuery.For<TitleAspect>().Facet(x => x.Title).StartsWith("Pro"),
    TypedQuery.For<StatusAspect>().Facet(x => x.Code).In(["Draft", "Ready"]),
    TypedQuery.For<PriceAspect>().Facet(x => x.Amount).Exists());
```

## Validation

```csharp
var validation = validator.Validate(query);
if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code}: {failure.Message}");
}
```
