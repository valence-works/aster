# Quickstart: Policy Validation Summaries

## Minimal Successful Summary

```csharp
using Aster.Core.Models.Policies;

var summary = ResourcePolicyValidationResult.Success.ToSummary();

Console.WriteLine(summary.IsValid);
Console.WriteLine(summary.TotalDiagnosticCount);
```

Expected result:

- `IsValid` is `true`
- `HasDiagnostics` is `false`
- `TotalDiagnosticCount` is `0`
- all grouped count lists are empty

## Mixed Diagnostic Summary

```csharp
using Aster.Core.Models.Policies;

var result = new ResourcePolicyValidationResult
{
    Diagnostics =
    [
        new ResourcePolicyDiagnostic
        {
            Code = ResourcePolicyDiagnosticCodes.PolicyInvalid,
            Message = "Policy ID is required.",
            Path = "policyDeclarations/0/policyId",
        },
        new ResourcePolicyDiagnostic
        {
            Code = ResourcePolicyDiagnosticCodes.PolicyConflict,
            Message = "Policy ID is declared more than once.",
            Path = "policyDeclarations/1/policyId",
            PolicyId = "archive-old-products",
        },
        new ResourcePolicyDiagnostic
        {
            Code = ResourcePolicyDiagnosticCodes.PolicyConflict,
            Message = "Policy ID is declared more than once.",
            Path = "policyDeclarations/2/policyId",
            PolicyId = "archive-old-products",
        },
    ],
};

var summary = result.ToSummary();

Console.WriteLine(summary.TotalDiagnosticCount);
Console.WriteLine(summary.DiagnosticCodeCounts.Count);
Console.WriteLine(summary.PolicyIdCounts.Count);
```

Expected result:

- `TotalDiagnosticCount` is `3`
- diagnostic-code counts include `policy-conflict: 2` and `policy-invalid: 1`
- policy-id counts include `archive-old-products: 2`
- path counts are ordered deterministically

## Validation

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~PolicyValidationSummaryTests"
dotnet test Aster.sln --filter "FullyQualifiedName~PolicyValidationTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
```
