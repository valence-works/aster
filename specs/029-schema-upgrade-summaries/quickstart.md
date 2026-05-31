# Quickstart: Schema Upgrade Summaries

## Summarize Schema Status Results

```csharp
var statusResults = new[]
{
    await schemaVersionService.GetSchemaStatusAsync(resourceA),
    await schemaVersionService.GetSchemaStatusAsync(resourceB),
    await schemaVersionService.GetSchemaStatusAsync(resourceC),
};

var summary = statusResults.ToSummary();

Console.WriteLine(summary.TotalInspectedCount);
Console.WriteLine(summary.UpgradeNeededCount);
Console.WriteLine(summary.BlockingCount);
```

## Summarize Schema Upgrade Results

```csharp
var upgradeResults = new[]
{
    await schemaVersionService.UpgradeAsync("product-1", new ResourceSchemaUpgradeRequest { BaseVersion = 1 }),
    await schemaVersionService.UpgradeAsync("product-2", new ResourceSchemaUpgradeRequest { BaseVersion = 1 }),
};

var summary = upgradeResults.ToSummary();

Console.WriteLine(summary.TotalProcessedCount);
Console.WriteLine(summary.UpgradedResourceCount);
Console.WriteLine(summary.CarriedForwardAspectKeyCount);
```

## Expected Validation

Run focused tests first:

```sh
dotnet test Aster.sln --filter "FullyQualifiedName~ResourceSchemaUpgradeSummaryTests"
```

Run broader validation before opening a PR:

```sh
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```

## Scope Check

This feature only summarizes objects already returned by schema status and upgrade workflows. It does not add a batch upgrade service, provider implementation, storage change, scheduler, audit sink, raw SQL surface, public `IQueryable<Resource>`, or mutation behavior.
