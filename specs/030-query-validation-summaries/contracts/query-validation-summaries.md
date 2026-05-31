# Contract: Query Validation Summaries

## Public SDK Behavior

The core SDK exposes pure summary records and an extension helper for query validation result objects.

Expected public helper:

```csharp
QueryValidationSummary ToSummary(this QueryValidationResult result)
```

## Query Validation Summary Contract

`QueryValidationSummary` MUST:

- Count all failures in the supplied validation result.
- Expose `IsValid` and `HasFailures` booleans derived from the supplied result.
- Count nonblank failure codes using deterministic ordinal ordering.
- Count nonblank failure paths using deterministic ordinal ordering.
- Count nonblank failure features using deterministic ordinal ordering.
- Ignore blank keys in key-specific count rows while preserving total failure count.
- Throw normal argument validation errors when the validation result itself is null.
- Avoid store reads, provider calls, service resolution, query planning, query execution, writes, raw SQL, public `IQueryable<Resource>`, and result mutation.

## Non-Goals

The helper MUST NOT:

- Execute validation.
- Execute queries.
- Rewrite queries.
- Suggest alternative query plans.
- Compare multiple providers.
- Register services.
- Add provider capabilities.
- Persist reports or audit records.
