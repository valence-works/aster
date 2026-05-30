# Quickstart: Version History Summaries

## Summarize One Resource History

```csharp
var historyService = provider.GetRequiredService<IResourceVersionHistoryService>();

var history = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest
{
    ResourceId = "product-1",
});

var summary = history.ToSummary();

Console.WriteLine($"Versions: {summary.TotalVersionCount}");
Console.WriteLine($"Protected: {summary.ProtectedVersionCount}");
Console.WriteLine($"Candidates: {summary.PossibleCandidateCount}");
```

## Summarize A Batch Result

```csharp
var batch = await historyService.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
{
    ResourceIds = ["product-1", "product-2", "missing-product"],
});

var summary = batch.ToSummary();

Console.WriteLine($"Selected resources: {summary.SelectedResourceCount}");
Console.WriteLine($"Resources with versions: {summary.ResourcesWithVersionsCount}");
Console.WriteLine($"Missing resources: {summary.MissingResourceCount}");
```

## Manual Results

Summary helpers are pure transformations and do not require service registration:

```csharp
var summary = new ResourceVersionHistoryResult
{
    ResourceId = "manual",
    Versions = [],
}.ToSummary();
```

No provider, storage, policy evaluation, or mutation behavior is involved.
