# Contract: Version History Summaries

Version history summaries provide pure aggregate views over existing history result objects.

## Host-Facing Behavior

Hosts can:

- summarize one `ResourceVersionHistoryResult`;
- summarize one `ResourceVersionHistoryBatchResult`;
- use summaries over service-produced or manually constructed result objects;
- display deterministic version-state and lifecycle-state counts without recomputing them.

The SDK must:

- fail fast for null result inputs;
- treat null result collections as empty;
- preserve source tenant and resource identity;
- count only values already present on `ResourceVersionSummary`;
- order lifecycle state counts deterministically.

The SDK must not:

- read from providers or stores;
- evaluate policy eligibility;
- mutate versions, activation state, or lifecycle markers;
- introduce storage, service registration, public SQL, public `IQueryable<Resource>`, query planning, runtime scanning, automatic discovery, background jobs, or reporting infrastructure.

## Proposed Public SDK Contract

```csharp
public sealed record ResourceVersionLifecycleStateCount
{
    public required ResourceLifecycleMarkerState State { get; init; }
    public required int Count { get; init; }
}

public sealed record ResourceVersionHistorySummary
{
    public TenantScope TenantScope { get; init; } = TenantScope.Default;
    public required string ResourceId { get; init; }
    public required int TotalVersionCount { get; init; }
    public required int LatestVersionCount { get; init; }
    public required int DraftVersionCount { get; init; }
    public required int ActiveVersionCount { get; init; }
    public required int ProtectedVersionCount { get; init; }
    public required int PossibleCandidateCount { get; init; }
    public IReadOnlyList<ResourceVersionLifecycleStateCount> LifecycleStateCounts { get; init; } = [];
}

public sealed record ResourceVersionHistoryBatchSummary
{
    public TenantScope TenantScope { get; init; } = TenantScope.Default;
    public required int SelectedResourceCount { get; init; }
    public required int ResourcesWithVersionsCount { get; init; }
    public required int MissingResourceCount { get; init; }
    public required int TotalVersionCount { get; init; }
    public required int ActiveVersionCount { get; init; }
    public required int ProtectedVersionCount { get; init; }
    public required int PossibleCandidateCount { get; init; }
    public IReadOnlyList<ResourceVersionLifecycleStateCount> LifecycleStateCounts { get; init; } = [];
}

public static class ResourceVersionHistorySummaryExtensions
{
    public static ResourceVersionHistorySummary ToSummary(this ResourceVersionHistoryResult result);

    public static ResourceVersionHistoryBatchSummary ToSummary(this ResourceVersionHistoryBatchResult result);
}
```

## Counting Rules

- `TotalVersionCount`: count of version summaries.
- `LatestVersionCount`: summaries where `IsLatest` is true.
- `DraftVersionCount`: summaries where `IsDraft` is true.
- `ActiveVersionCount`: summaries with at least one active channel.
- `ProtectedVersionCount`: summaries where `IsProtectedFromPruning` is true.
- `PossibleCandidateCount`: summaries where `MaintenanceDisposition` is `PossibleCandidate`.
- `ResourcesWithVersionsCount`: histories with at least one version.
- `MissingResourceCount`: histories with zero versions.
- `LifecycleStateCounts`: grouped by `LifecycleState`, ordered by enum value.
