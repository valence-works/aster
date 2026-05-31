# Quickstart: Lifecycle Marker Result Summaries

## Minimal Successful Summary

```csharp
using Aster.Core.Models.Instances;

var result = new ResourceLifecycleMarkerResult
{
    Marker = new ResourceLifecycleMarker
    {
        ResourceId = "product-1",
        State = ResourceLifecycleMarkerState.Archived,
        MarkedAt = DateTimeOffset.UtcNow,
    },
};

var summary = result.ToSummary();

Console.WriteLine(summary.IsFullySuccessful);
Console.WriteLine(summary.MarkerPresentCount);
```

Expected result:

- `IsFullySuccessful` is `true`
- `HasFailures` is `false`
- `HasDiagnostics` is `false`
- `TotalResultCount` is `1`
- `SucceededCount` is `1`
- `MarkerPresentCount` is `1`
- marker-state counts include `Archived: 1`
- marker-resource counts include `product-1: 1`

## Mixed Result Summary

```csharp
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;

var results = new[]
{
    new ResourceLifecycleMarkerResult
    {
        Marker = new ResourceLifecycleMarker
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        },
    },
    new ResourceLifecycleMarkerResult
    {
        Diagnostics =
        [
            ResourcePolicyValidator.Diagnostic(
                ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict,
                "Resource is already marked.",
                "state",
                resourceId: "product-1"),
        ],
    },
};

var summary = results.ToSummary();

Console.WriteLine(summary.TotalResultCount);
Console.WriteLine(summary.FailedCount);
Console.WriteLine(summary.TotalDiagnosticCount);
```

Expected result:

- `TotalResultCount` is `2`
- `SucceededCount` is `1`
- `FailedCount` is `1`
- `TotalDiagnosticCount` is `1`
- diagnostic-code counts include `lifecycle-marker-conflict: 1`
- diagnostic-path counts include `state: 1`
- diagnostic-resource counts include `product-1: 1`

## Validation

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleMarkerResultSummaryTests"
dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleMarkerServiceTests|FullyQualifiedName~LifecycleMarkerConflictTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
```
