# Contract: Index Projection Summaries

## Public SDK Behavior

The core SDK exposes pure summary records and extension helpers for index projection result objects.

Expected public helpers:

```csharp
IndexProjectionValidationSummary ToSummary(this IndexProjectionValidationResult result)
IndexProjectionEvaluationSummary ToSummary(this IndexProjectionEvaluationResult result)
```

## Projection Validation Summary Contract

`IndexProjectionValidationSummary` MUST:

- Count all failures in the supplied validation result.
- Expose `IsValid` and `HasFailures` booleans derived from the supplied result.
- Count nonblank failure codes using deterministic ordinal ordering.
- Count nonblank failure field names using deterministic ordinal ordering.
- Count nonblank failure sources using deterministic ordinal ordering.
- Ignore blank keys in key-specific count rows while preserving total failure count.
- Throw normal argument validation errors when the validation result itself is null.
- Avoid store reads, provider calls, service resolution, physical index creation, query planning, query execution, writes, raw SQL, public `IQueryable<Resource>`, and result mutation.

## Projection Evaluation Summary Contract

`IndexProjectionEvaluationSummary` MUST:

- Count all successful values and failures in the supplied evaluation result.
- Expose `IsValid`, `HasFailures`, and `HasValues` booleans derived from the supplied result.
- Count successful values by field type using deterministic enum ordering.
- Count successful values by nonblank field name using deterministic ordinal ordering.
- Count nonblank failure codes, field names, and sources using deterministic ordinal ordering.
- Ignore blank keys in key-specific count rows while preserving total value and failure counts.
- Throw normal argument validation errors when the evaluation result itself is null.
- Avoid store reads, provider calls, service resolution, physical index creation, query planning, query execution, writes, raw SQL, public `IQueryable<Resource>`, and result mutation.

## Non-Goals

The helpers MUST NOT:

- Validate projection declarations.
- Evaluate projections.
- Create or manage physical indexes.
- Execute or rewrite queries.
- Compare providers.
- Register services.
- Add provider capabilities.
- Persist reports or audit records.
