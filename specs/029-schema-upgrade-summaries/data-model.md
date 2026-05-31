# Data Model: Schema Upgrade Summaries

## ResourceSchemaStatusCount

Deterministic count for one schema status.

Fields:

- `Status`: `ResourceSchemaStatus`
- `Count`: number of inspected results with the status

Validation:

- `Count` is zero or positive.
- Rows are ordered by `Status`.

## ResourceSchemaStatusSummary

Aggregate view over schema status inspection results.

Fields:

- `TotalInspectedCount`: total supplied status results
- `UpgradeNeededCount`: count where status is `OlderThanLatest`
- `BlockingCount`: count where status is `MissingDefinition` or `MissingDefinitionVersion`
- `UnknownLineageCount`: count where status is `UnknownResourceLineage`
- `IsUpgradeFree`: true when `UpgradeNeededCount` is zero
- `HasBlockingStatuses`: true when `BlockingCount` is greater than zero
- `StatusCounts`: deterministic status counts

Validation:

- Empty inputs produce zero counts and empty `StatusCounts`.
- Summary creation does not mutate status results.

## ResourceSchemaUpgradeStatusCount

Deterministic count for one schema upgrade result status.

Fields:

- `Status`: `ResourceSchemaUpgradeStatus`
- `Count`: number of upgrade results with the status

Validation:

- `Count` is zero or positive.
- Rows are ordered by `Status`.

## ResourceSchemaDefinitionVersionCount

Deterministic count for source or target definition versions represented by upgrade results.

Fields:

- `Version`: definition version number when known
- `IsUnknown`: true when the source version was unknown
- `Count`: number of upgrade results in this bucket

Validation:

- Exactly one of known `Version` or `IsUnknown = true` identifies a bucket.
- Unknown bucket sorts before numeric buckets.
- Numeric buckets sort by version ascending.

## ResourceSchemaCarriedForwardAspectKeyCount

Deterministic count for one carried-forward aspect key.

Fields:

- `AspectKey`: nonblank carried-forward aspect key
- `Count`: number of times the key was carried forward across upgrade results

Validation:

- Blank aspect keys are ignored.
- Rows are ordered by ordinal aspect key.

## ResourceSchemaUpgradeSummary

Aggregate view over schema upgrade result objects.

Fields:

- `TotalProcessedCount`: total supplied upgrade results
- `UpgradedResourceCount`: number of results with an upgraded resource object
- `CarriedForwardAspectKeyCount`: total nonblank carried-forward aspect key occurrences
- `IsNoOpOnly`: true when one or more results were processed and all are no-op outcomes
- `HasUpgrades`: true when at least one result has status `Upgraded`
- `StatusCounts`: deterministic counts by upgrade status
- `SourceDefinitionVersionCounts`: deterministic counts by source version, including unknown source versions
- `TargetDefinitionVersionCounts`: deterministic counts by target version
- `CarriedForwardAspectKeyCounts`: deterministic counts by nonblank carried-forward aspect key

Validation:

- Empty inputs produce zero counts and empty count lists.
- Summary creation does not mutate upgrade results or resource objects.
