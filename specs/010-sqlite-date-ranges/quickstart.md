# Quickstart: SQLite Date-Like Facet Ranges

## Store Date-Like Facets

```csharp
await manager.CreateAsync("Event", new CreateResourceRequest
{
    InitialAspects =
    {
        ["Schedule"] = new { StartsAt = new DateTimeOffset(2026, 02, 01, 10, 00, 00, TimeSpan.Zero) },
    },
});
```

## Query A Date Range

```csharp
var resourceQuery = new ResourceQuery
{
    DefinitionId = "Event",
    Filter = new FacetValueFilter(
        "Schedule",
        "StartsAt",
        new RangeValue(
            Min: new DateTimeOffset(2026, 02, 01, 00, 00, 00, TimeSpan.Zero),
            Max: new DateTimeOffset(2026, 02, 28, 23, 59, 59, TimeSpan.Zero)),
        ComparisonOperator.Range),
};

var results = await query.QueryAsync(resourceQuery);
```

## Validate First

```csharp
var validation = validator.Validate(resourceQuery);
if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code}: {failure.Message}");
}
```

## Accepted Stored Shape

Date-like SQLite facet ranges expect stored facet values to be JSON strings in the same ISO-8601-style shape emitted by `System.Text.Json` for `DateTime` or `DateTimeOffset`. Invalid strings, nulls, numbers, booleans, arrays, objects, and missing facets do not match.
