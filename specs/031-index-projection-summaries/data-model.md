# Data Model: Index Projection Summaries

## IndexProjectionFailureCodeCount

Deterministic count for one nonblank projection failure code.

Fields:

- `Code`: stable nonblank failure code
- `Count`: number of failures with the code

Validation:

- Blank codes are ignored.
- Rows are ordered ordinally by `Code`.

## IndexProjectionFailureFieldCount

Deterministic count for one nonblank projection field name associated with failures.

Fields:

- `FieldName`: nonblank projection field name
- `Count`: number of failures with the field name

Validation:

- Blank field names are ignored.
- Rows are ordered ordinally by `FieldName`.

## IndexProjectionFailureSourceCount

Deterministic count for one nonblank projection source description.

Fields:

- `Source`: nonblank source description
- `Count`: number of failures with the source

Validation:

- Blank sources are ignored.
- Rows are ordered ordinally by `Source`.

## IndexProjectionValueFieldTypeCount

Deterministic count for one successful projection value field type.

Fields:

- `FieldType`: `IndexFieldType`
- `Count`: number of successful values with the field type

Validation:

- Rows are ordered by `FieldType`.

## IndexProjectionValueFieldCount

Deterministic count for one successful projection value field name.

Fields:

- `FieldName`: nonblank successful value field name
- `Count`: number of successful values with the field name

Validation:

- Blank field names are ignored.
- Rows are ordered ordinally by `FieldName`.

## IndexProjectionValidationSummary

Aggregate view over a projection validation result.

Fields:

- `TotalFailureCount`: total validation failures
- `IsValid`: true when there are no failures
- `HasFailures`: true when one or more failures exist
- `FailureCodeCounts`: deterministic counts by nonblank code
- `FailureFieldCounts`: deterministic counts by nonblank field name
- `FailureSourceCounts`: deterministic counts by nonblank source

Validation:

- Successful validation produces zero counts and `IsValid = true`.
- Blank code/field/source keys are excluded from key-specific counts.
- Summary creation does not mutate validation result or failure objects.

## IndexProjectionEvaluationSummary

Aggregate view over a projection evaluation result.

Fields:

- `TotalValueCount`: total successful projection values
- `TotalFailureCount`: total projection failures
- `IsValid`: true when there are no failures
- `HasFailures`: true when one or more failures exist
- `HasValues`: true when one or more values exist
- `ValueFieldTypeCounts`: deterministic counts by successful value field type
- `ValueFieldCounts`: deterministic counts by successful value field name
- `FailureCodeCounts`: deterministic counts by nonblank failure code
- `FailureFieldCounts`: deterministic counts by nonblank failure field name
- `FailureSourceCounts`: deterministic counts by nonblank failure source

Validation:

- Empty evaluation results produce zero counts and `IsValid = true`.
- Mixed value/failure results preserve both totals.
- Summary creation does not mutate evaluation result, value, or failure objects.
