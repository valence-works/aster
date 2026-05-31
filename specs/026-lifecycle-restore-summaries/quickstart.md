# Quickstart: Lifecycle Restore Summaries

## Minimal Application Summary

```csharp
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;

var result = new ResourceLifecycleRestoreApplicationResult
{
    Candidates =
    [
        new ResourceLifecycleRestoreCandidateResult
        {
            Index = 0,
            ResourceId = "product-1",
            ExpectedState = ResourceLifecycleMarkerState.Archived,
            Status = ResourceLifecycleRestoreCandidateStatus.Restored,
        },
        new ResourceLifecycleRestoreCandidateResult
        {
            Index = 1,
            ResourceId = "product-2",
            ExpectedState = ResourceLifecycleMarkerState.Archived,
            Status = ResourceLifecycleRestoreCandidateStatus.Failed,
            Diagnostics =
            [
                new ResourcePolicyDiagnostic
                {
                    Code = ResourcePolicyDiagnosticCodes.LifecycleRestoreMarkerMismatch,
                    Message = "Marker state did not match.",
                },
            ],
        },
    ],
};

var summary = result.ToSummary();

Console.WriteLine(summary.RestoredCount);
Console.WriteLine(summary.FailedCount);
Console.WriteLine(summary.HasFailures);
```

## Minimal Preview Summary

```csharp
using Aster.Core.Models.Instances;

var preview = new ResourceLifecycleRestorePreviewResult
{
    Candidates =
    [
        new ResourceLifecycleRestoreCandidateResult
        {
            Index = 0,
            ResourceId = "product-1",
            ExpectedState = ResourceLifecycleMarkerState.SoftDeleted,
            Status = ResourceLifecycleRestoreCandidateStatus.Restorable,
        },
        new ResourceLifecycleRestoreCandidateResult
        {
            Index = 1,
            ResourceId = "product-1",
            ExpectedState = ResourceLifecycleMarkerState.SoftDeleted,
            Status = ResourceLifecycleRestoreCandidateStatus.AlreadyRestored,
        },
    ],
};

var summary = preview.ToSummary();

Console.WriteLine(summary.RestorableCount);
Console.WriteLine(summary.CandidateResourceCount);
Console.WriteLine(summary.IsFullySuccessful);
```

## Validation

Run focused summary tests:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~ResourceLifecycleRestoreSummaryTests"
```

Run full validation:

```bash
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```
