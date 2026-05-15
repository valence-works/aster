# Quickstart: Provider Validation Execution Alignment

## Inspect Provider Capabilities

```csharp
var capabilities = serviceProvider
    .GetRequiredService<IResourceQueryCapabilitiesProvider>()
    .Capabilities;

Console.WriteLine(capabilities.ProviderKey);
Console.WriteLine(capabilities.ProviderName);
```

## Validate Before Execution

```csharp
var query = new ResourceQuery
{
    DefinitionId = "Product",
    Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
};

var validator = serviceProvider.GetRequiredService<IResourceQueryValidator>();
var validation = validator.Validate(query);

if (!validation.IsValid)
{
    foreach (var failure in validation.Failures)
        Console.WriteLine($"{failure.Code} ({failure.Feature}): {failure.Message}");

    return;
}
```

## Execution Still Enforces Unsupported Shapes

```csharp
try
{
    var queryService = serviceProvider.GetRequiredService<IResourceQueryService>();
    var results = await queryService.QueryAsync(query);
}
catch (UnsupportedQueryFeatureException ex)
{
    Console.WriteLine($"{ex.Code} ({ex.Feature}): {ex.Message}");
}
```

If caller preflight is skipped, the provider still validates and rejects unsupported query shapes before or during execution.

## Custom Provider Registration

Custom providers must use a stable provider key and register matching capabilities.

```csharp
services.AddSingleton<IResourceQueryService, MyQueryService>();
services.AddSingleton<IResourceQueryCapabilitiesProvider, MyQueryCapabilitiesProvider>();
```

Validation fails closed with `capabilities-not-declared` when the active provider's key has no matching capability declaration.

## Provider Consistency Tests

Provider tests should compare validation and execution:

```csharp
var validation = validator.Validate(unsupportedQuery);
var exception = await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(
    () => queryService.QueryAsync(unsupportedQuery).AsTask());

Assert.Contains(validation.Failures, failure => failure.Code == exception.Code);
Assert.Equal("sort", exception.Feature);
```
