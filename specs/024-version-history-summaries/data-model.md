# Data Model: Version History Summaries

## Resource Version History Summary

Aggregate view over one `ResourceVersionHistoryResult`.

Fields:

- `TenantScope`: Effective tenant from the source result.
- `ResourceId`: Source logical resource identifier.
- `TotalVersionCount`: Number of version summaries.
- `LatestVersionCount`: Number of summaries marked latest.
- `DraftVersionCount`: Number of summaries marked draft.
- `ActiveVersionCount`: Number of summaries with one or more active channels.
- `ProtectedVersionCount`: Number of summaries protected from pruning.
- `PossibleCandidateCount`: Number of summaries with possible-candidate maintenance disposition.
- `LifecycleStateCounts`: Deterministic counts by lifecycle marker state.

Validation:

- Source result must not be null.
- Null `Versions` collection is treated as empty.

## Resource Version History Batch Summary

Aggregate view over one `ResourceVersionHistoryBatchResult`.

Fields:

- `TenantScope`: Effective tenant from the source result.
- `SelectedResourceCount`: Number of histories in the batch result.
- `ResourcesWithVersionsCount`: Number of histories containing one or more versions.
- `MissingResourceCount`: Number of histories containing no versions.
- `TotalVersionCount`: Total number of version summaries across histories.
- `ActiveVersionCount`: Number of version summaries with one or more active channels.
- `ProtectedVersionCount`: Number of protected version summaries.
- `PossibleCandidateCount`: Number of possible-candidate version summaries.
- `LifecycleStateCounts`: Deterministic counts by lifecycle marker state across all version summaries.

Validation:

- Source batch result must not be null.
- Null `Histories` collection is treated as empty.
- Null per-history `Versions` collection is treated as empty.

## Lifecycle State Count

Deterministic count for one lifecycle state.

Fields:

- `State`: Lifecycle marker state.
- `Count`: Number of version summaries with the state.

Ordering:

- Counts are ordered by lifecycle state enum value.

## State Transitions

None. Summaries are pure projections and must not mutate resources, versions, activation state, lifecycle markers, policies, or storage.
