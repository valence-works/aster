# Data Model: Query Validation Summaries

## QueryValidationFailureCodeCount

Deterministic count for one nonblank validation failure code.

Fields:

- `Code`: stable nonblank validation failure code
- `Count`: number of failures with the code

Validation:

- Blank codes are ignored.
- Rows are ordered ordinally by `Code`.

## QueryValidationFailurePathCount

Deterministic count for one nonblank query path.

Fields:

- `Path`: nonblank query path
- `Count`: number of failures with the path

Validation:

- Blank paths are ignored.
- Rows are ordered ordinally by `Path`.

## QueryValidationFailureFeatureCount

Deterministic count for one nonblank feature category.

Fields:

- `Feature`: nonblank failure feature category
- `Count`: number of failures with the feature

Validation:

- Blank features are ignored.
- Rows are ordered ordinally by `Feature`.

## QueryValidationSummary

Aggregate view over a query validation result.

Fields:

- `TotalFailureCount`: total validation failures
- `IsValid`: true when there are no failures
- `HasFailures`: true when one or more failures exist
- `FailureCodeCounts`: deterministic counts by nonblank code
- `FailurePathCounts`: deterministic counts by nonblank path
- `FailureFeatureCounts`: deterministic counts by nonblank feature

Validation:

- `QueryValidationResult.Success` produces zero counts and `IsValid = true`.
- Blank code/path/feature keys are excluded from key-specific counts.
- Summary creation does not mutate validation result or failure objects.
