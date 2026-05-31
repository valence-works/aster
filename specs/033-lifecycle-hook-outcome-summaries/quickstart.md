# Quickstart: Lifecycle Hook Outcome Summaries

## Minimal Successful Summary

```csharp
using Aster.Core.Models.Lifecycle;

var summary = LifecycleHookOutcome.Continue().ToSummary();

Console.WriteLine(summary.IsFullySuccessful);
Console.WriteLine(summary.TotalOutcomeCount);
```

Expected result:

- `IsFullySuccessful` is `true`
- `HasRejectedOutcomes` is `false`
- `HasFailedOutcomes` is `false`
- `TotalOutcomeCount` is `1`
- `ContinueCount` is `1`

## Mixed Outcome Summary

```csharp
using Aster.Core.Models.Lifecycle;

var outcomes = new[]
{
    LifecycleHookOutcome.Continue(),
    LifecycleHookOutcome.Reject(
        "policy-rejected",
        "Policy gate rejected the operation.",
        [
            new LifecycleHookDiagnostic
            {
                Code = "policy-rejected",
                Message = "Rejected by policy hook.",
                LifecyclePoint = LifecyclePoint.BeforeSave,
                HookType = "PolicyLifecycleHook",
            },
        ]),
    LifecycleHookOutcome.Fail("audit-failed", "Audit hook failed."),
};

var summary = outcomes.ToSummary();

Console.WriteLine(summary.TotalOutcomeCount);
Console.WriteLine(summary.RejectedCount);
Console.WriteLine(summary.FailedCount);
Console.WriteLine(summary.TotalDiagnosticCount);
```

Expected result:

- `TotalOutcomeCount` is `3`
- `ContinueCount` is `1`
- `RejectedCount` is `1`
- `FailedCount` is `1`
- outcome-code counts include `audit-failed: 1` and `policy-rejected: 1`
- diagnostic-code counts include `policy-rejected: 1`
- lifecycle-point counts include `BeforeSave: 1`
- hook-type counts include `PolicyLifecycleHook: 1`

## Validation

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~LifecycleHookOutcomeSummaryTests"
dotnet test Aster.sln --filter "FullyQualifiedName~ResourceLifecycleHookDispatcherTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
```
