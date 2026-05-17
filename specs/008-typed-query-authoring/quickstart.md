# Quickstart: Typed Query Authoring Ergonomics

Typed query helpers reduce string repetition while still producing the normal portable query AST.

## Typed Facet Sort

```csharp
var sort = TypedQuery.For<TitleAspect>()
    .Facet(x => x.Title)
    .Ascending();

var query = new ResourceQuery
{
    DefinitionId = "Product",
    Sorts = [sort],
};
```

Equivalent manual shape:

```csharp
new SortExpression("Title", SortDirection.Ascending, AspectKey: "TitleAspect");
```

## Descending Facet Sort With Overrides

```csharp
var sort = TypedQuery.For<PriceAspect>(aspectKey: "PriceAspect:Sale")
    .Facet(x => x.Amount, facetIdentifier: "sale_amount")
    .Descending();
```

## Logical Composition

```csharp
var filter = TypedQuery.And(
    TypedQuery.For<TitleAspect>().Facet(x => x.Title).Contains("Gadget"),
    TypedQuery.For<PriceAspect>().Facet(x => x.Amount).Range(min: 10m, max: 100m));

var query = new ResourceQuery
{
    DefinitionId = "Product",
    Filter = filter,
    Sorts =
    [
        TypedQuery.For<PriceAspect>().Facet(x => x.Amount).Descending(),
    ],
};
```

## Validation And Execution

Helper-generated queries use the same validation and execution path as manual AST construction:

```csharp
var validation = validator.Validate(query);
if (validation.IsValid)
{
    var results = await queryService.QueryAsync(query);
}
```
