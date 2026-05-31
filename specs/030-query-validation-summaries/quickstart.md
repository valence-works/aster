# Quickstart: Query Validation Summaries

## Summarize a Validation Result

```csharp
var validation = queryValidator.Validate(query);
var summary = validation.ToSummary();

Console.WriteLine(summary.TotalFailureCount);
Console.WriteLine(summary.IsValid);

foreach (var code in summary.FailureCodeCounts)
{
    Console.WriteLine($"{code.Code}: {code.Count}");
}
```

## Expected Validation

Run focused tests first:

```sh
dotnet test Aster.sln --filter "FullyQualifiedName~QueryValidationSummaryTests"
```

Run broader validation before opening a PR:

```sh
dotnet test Aster.sln --filter "FullyQualifiedName~ResourceQueryValidatorTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```

## Scope Check

This feature only summarizes existing query validation result objects. It does not execute validation, execute queries, rewrite queries, add a query planner, add provider support, introduce service registration, expose raw SQL, expose public `IQueryable<Resource>`, persist reports, or mutate validation results.
