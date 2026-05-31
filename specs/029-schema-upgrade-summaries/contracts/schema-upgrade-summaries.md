# Contract: Schema Upgrade Summaries

## Public SDK Behavior

The core SDK exposes pure summary records and extension helpers for schema status and schema upgrade result objects.

Expected public helpers:

```csharp
ResourceSchemaStatusSummary ToSummary(this IEnumerable<ResourceSchemaStatusResult>? results)
ResourceSchemaUpgradeSummary ToSummary(this IEnumerable<ResourceSchemaUpgradeResult>? results)
```

## Schema Status Summary Contract

`ResourceSchemaStatusSummary` MUST:

- Count all supplied `ResourceSchemaStatusResult` objects.
- Count statuses deterministically by `ResourceSchemaStatus`.
- Treat `OlderThanLatest` as upgrade-needed.
- Treat `MissingDefinition` and `MissingDefinitionVersion` as blocking.
- Treat `UnknownResourceLineage` as unknown lineage.
- Return zero counts and empty count lists for null or empty collections.
- Avoid store reads, provider calls, service resolution, writes, scheduling, auditing, raw SQL, public `IQueryable<Resource>`, and result mutation.

## Schema Upgrade Summary Contract

`ResourceSchemaUpgradeSummary` MUST:

- Count all supplied `ResourceSchemaUpgradeResult` objects.
- Count statuses deterministically by `ResourceSchemaUpgradeStatus`.
- Count upgraded resources based on non-null `Resource` values.
- Count target definition versions deterministically.
- Count source definition versions deterministically and include an explicit unknown bucket for null source versions.
- Count nonblank carried-forward aspect keys deterministically by ordinal key.
- Return zero counts and empty count lists for null or empty collections.
- Avoid store reads, provider calls, service resolution, writes, scheduling, auditing, raw SQL, public `IQueryable<Resource>`, and result mutation.

## Non-Goals

The helpers MUST NOT:

- Execute schema status inspection.
- Execute schema upgrades.
- Catch or represent failed upgrade exceptions.
- Introduce batch upgrade orchestration.
- Persist reports or audit records.
- Add provider contracts or storage schema.
- Introduce a reporting framework.
