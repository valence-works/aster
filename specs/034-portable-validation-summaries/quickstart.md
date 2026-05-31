# Quickstart: Portable Validation Summaries

## Minimal Valid Summary

```csharp
using Aster.Core.Models.Portability;

var result = new PortableSnapshotValidationResult
{
    IsValid = true,
};

var summary = result.ToSummary();

Console.WriteLine(summary.IsValid);
Console.WriteLine(summary.TotalDiagnosticCount);
```

Expected result:

- `IsValid` is `true`
- `HasErrors` is `false`
- `TotalDiagnosticCount` is `0`
- diagnostic severity counts are empty
- diagnostic code counts are empty

## Invalid Summary With Diagnostics

```csharp
using Aster.Core.Models.Portability;

var result = new PortableSnapshotValidationResult
{
    IsValid = false,
    Diagnostics =
    [
        new PortableDiagnostic
        {
            Code = PortableDiagnosticCodes.MissingResourceReference,
            Severity = PortableDiagnosticSeverity.Error,
            Message = "Activation entry references a missing resource.",
        },
        new PortableDiagnostic
        {
            Code = PortableDiagnosticCodes.DivergentIdentityCollision,
            Severity = PortableDiagnosticSeverity.Warning,
            Message = "Import may remap a divergent identity.",
        },
    ],
};

var summary = result.ToSummary();

Console.WriteLine(summary.IsValid);
Console.WriteLine(summary.HasErrors);
Console.WriteLine(summary.TotalDiagnosticCount);
```

Expected result:

- `IsValid` is `false`
- `HasErrors` is `true`
- `TotalDiagnosticCount` is `2`
- severity counts include `Warning: 1` and `Error: 1`
- code counts include `divergent-identity-collision: 1` and `missing-resource-reference: 1`

## Validation

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~PortableResultSummaryTests"
dotnet test Aster.sln --filter "FullyQualifiedName~PortabilityValidationTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
```
