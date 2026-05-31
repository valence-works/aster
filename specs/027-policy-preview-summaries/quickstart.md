# Quickstart: Policy Preview Summaries

## Minimal Preview Summary

```csharp
using Aster.Core.Models.Policies;

var preview = new ResourcePolicyEvaluationPreview
{
    Candidates =
    [
        new ResourcePolicyCandidateOutcome
        {
            PolicyId = "archive-old",
            PolicyKind = ResourcePolicyKind.Archival,
            Outcome = ResourcePolicyOutcome.Archive,
            ResourceId = "product-1",
            Reason = "Older than threshold.",
        },
        new ResourcePolicyCandidateOutcome
        {
            PolicyId = "prune-old-versions",
            PolicyKind = ResourcePolicyKind.VersionPruning,
            Outcome = ResourcePolicyOutcome.PrunePreview,
            ResourceId = "product-1",
            ResourceVersion = 1,
            Reason = "Outside retained version window.",
        },
    ],
};

var summary = preview.ToSummary();

Console.WriteLine(summary.TotalCandidateCount);
Console.WriteLine(summary.DistinctResourceCount);
Console.WriteLine(summary.DistinctResourceVersionTargetCount);
```

## Validation

Run focused summary tests:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~PolicyPreviewSummaryTests"
```

Run full validation:

```bash
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```
