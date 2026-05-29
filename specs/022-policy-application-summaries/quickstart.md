# Quickstart: Policy Application Summaries

## Summarize Marker-Based Policy Application

```csharp
ResourcePolicyApplicationResult result = await policyApplication.ApplyAsync(request);

var summary = result.ToSummary();
```

Expected use:

```csharp
summary.TotalCount
summary.AppliedCount
summary.AlreadySatisfiedCount
summary.SkippedCount
summary.FailedCount
summary.HasFailures
summary.IsFullySuccessful
summary.AffectedResourceCount
summary.DiagnosticCodeCounts
```

Hosts can use these values for a post-apply UI banner, operation details panel, or log message.

## Summarize Policy Pruning Application

```csharp
ResourcePolicyPruningApplicationResult result = await pruningApplication.ApplyAsync(request);

var summary = result.ToSummary();
```

Expected use:

```csharp
summary.TotalCount
summary.PrunedCount
summary.AlreadyPrunedCount
summary.SkippedCount
summary.FailedCount
summary.HasFailures
summary.IsFullySuccessful
summary.AffectedTargetCount
summary.DiagnosticCodeCounts
```

## Diagnostic Code Counts

```csharp
foreach (var code in summary.DiagnosticCodeCounts)
{
    Console.WriteLine($"{code.Code}: {code.Count}");
}
```

Diagnostic code counts are deterministic and ordered by code.

## Non-Goals

Summaries are not audit records. They do not persist, query, re-run policy logic, invoke hooks, schedule work, or provide authorization decisions.
