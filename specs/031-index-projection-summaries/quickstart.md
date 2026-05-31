# Quickstart: Index Projection Summaries

## Summarize Projection Validation

```csharp
var validator = new IndexProjectionValidator();
var validation = validator.Validate(capabilities.IndexProjections);
var summary = validation.ToSummary();

Console.WriteLine(summary.TotalFailureCount);

foreach (var code in summary.FailureCodeCounts)
{
    Console.WriteLine($"{code.Code}: {code.Count}");
}
```

## Summarize Projection Evaluation

```csharp
var evaluator = new IndexProjectionEvaluator();
var evaluation = evaluator.Evaluate(resource, capabilities.IndexProjections);
var summary = evaluation.ToSummary();

Console.WriteLine(summary.TotalValueCount);
Console.WriteLine(summary.TotalFailureCount);
```

## Expected Validation

Run focused tests first:

```sh
dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionSummaryTests"
```

Run broader validation before opening a PR:

```sh
dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionDeclarationTests"
dotnet test Aster.sln --filter "FullyQualifiedName~IndexProjectionEvaluationTests"
dotnet test Aster.sln
dotnet build Aster.sln /m:1
git diff --check
```

## Scope Check

This feature only summarizes existing projection validation and evaluation result objects. It does not validate declarations, evaluate projections, create physical indexes, add provider support, add a query planner, register services, expose raw SQL, expose public `IQueryable<Resource>`, persist reports, or mutate result objects.
