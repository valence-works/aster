# Contract: Batch Version History Inspection

Batch version history inspection provides read-only, tenant-scoped histories for an explicit set of logical resource identifiers.

## Host-Facing Behavior

Hosts can:

- request histories for selected logical resource IDs in one tenant;
- receive one per-resource history for each distinct requested identifier;
- preserve caller selection order without manually coordinating repeated single-resource calls;
- treat missing resources as empty histories;
- rely on existing version summary semantics for every returned history.

The SDK must:

- resolve one effective tenant per request;
- reject null requests, null resource ID collections, and blank resource IDs;
- collapse duplicate resource IDs using ordinal comparison;
- preserve first-seen distinct resource ID order;
- return version summaries ordered by version number;
- return active channel names ordered ordinally;
- return empty histories for missing resources;
- avoid mutations and side effects.

The SDK must not:

- evaluate policy eligibility as part of history inspection;
- run automatic policy execution, pruning, archive, restore, or activation behavior;
- leak state from another tenant;
- introduce public SQL, public `IQueryable<Resource>`, provider registries, runtime scanning, automatic discovery, schedulers, query planners, reporting infrastructure, or schema migrations.

## Proposed Public SDK Contract

```csharp
public interface IResourceVersionHistoryService
{
    ValueTask<ResourceVersionHistoryResult> GetHistoryAsync(
        ResourceVersionHistoryRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ResourceVersionHistoryBatchResult> GetHistoriesAsync(
        ResourceVersionHistoryBatchRequest request,
        CancellationToken cancellationToken = default);
}
```

Request shape:

```csharp
public sealed class ResourceVersionHistoryBatchRequest
{
    public TenantScope? TenantScope { get; set; }
    public IReadOnlyCollection<string>? ResourceIds { get; set; }
}
```

Result shape:

```csharp
public sealed record ResourceVersionHistoryBatchResult
{
    public TenantScope TenantScope { get; init; } = TenantScope.Default;
    public IReadOnlyList<ResourceVersionHistoryResult> Histories { get; init; } = [];
}
```

## Validation Rules

Batch history inspection must reject invalid request shape when:

- request is null;
- resource ID collection is null;
- any resource ID is null, empty, or whitespace.

Batch history inspection must not fail when:

- the resource ID collection is empty;
- a requested resource does not exist in the effective tenant;
- a requested resource has versions but no activation states;
- a requested resource has no lifecycle marker.

## Ordering Rules

- Batch histories are ordered by first-seen distinct resource ID order.
- Version summaries are ordered by `Version` ascending.
- Active channel names are ordered ordinally.
- Results for missing resources contain an empty `Versions` list.
