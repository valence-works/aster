# Quickstart: Portability Result Summaries

## Minimal Export Summary

```csharp
using Aster.Core.Models.Portability;

var export = new PortableSnapshotExportResult
{
    Snapshot = new PortableSnapshot
    {
        FormatVersion = PortableSnapshot.CurrentFormatVersion,
    },
};

var summary = export.ToSummary();

Console.WriteLine(summary.HasSnapshot);
Console.WriteLine(summary.DefinitionCount);
Console.WriteLine(summary.HasErrors);
```

## Minimal Import Preview Summary

```csharp
using Aster.Core.Models.Portability;

var preview = new PortableImportPreview
{
    CanImport = true,
    Counts = new PortablePlannedImportCounts
    {
        Definitions = 1,
        Resources = 1,
        ResourceVersions = 2,
    },
};

var summary = preview.ToSummary();

Console.WriteLine(summary.CanImport);
Console.WriteLine(summary.TotalPlannedItemCount);
```

## Minimal Import Summary

```csharp
using Aster.Core.Models.Portability;

var result = new PortableImportResult
{
    Status = PortableImportStatus.Imported,
    Counts = new PortableActualImportCounts
    {
        Definitions = 1,
        Resources = 1,
        ResourceVersions = 2,
    },
};

var summary = result.ToSummary();

Console.WriteLine(summary.IsImported);
Console.WriteLine(summary.TotalActualItemCount);
```

## Validation

Run focused summary tests:

```bash
dotnet test Aster.sln --filter "FullyQualifiedName~PortableResultSummaryTests"
```

Run full validation:

```bash
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```
