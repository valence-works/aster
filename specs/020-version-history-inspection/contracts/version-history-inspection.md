# Contract: Resource Version History Inspection

Version history inspection provides read-only, tenant-scoped resource timeline summaries for hosts.

## Host-Facing Behavior

Hosts can:

- request the history for one logical resource ID in one tenant;
- display version order, latest state, draft state, active channels, lifecycle state, and conservative maintenance hints;
- treat a missing resource in the effective tenant as an empty history result.

The SDK must:

- resolve one effective tenant per request;
- return summaries ordered by version number;
- report active channels without requiring the host to know channel names first;
- report current resource-level lifecycle marker state;
- identify latest or active versions as protected from destructive pruning;
- avoid mutations and side effects.

The SDK must not:

- evaluate policy eligibility as part of history inspection;
- run automatic policy execution or pruning;
- leak state from another tenant;
- introduce public SQL, public `IQueryable<Resource>`, provider registries, runtime scanning, schedulers, or schema migrations.

## Proposed Public SDK Contract

```csharp
public interface IResourceVersionHistoryService
{
    ValueTask<ResourceVersionHistoryResult> GetHistoryAsync(
        ResourceVersionHistoryRequest request,
        CancellationToken cancellationToken = default);
}
```

Request shape:

```csharp
public sealed class ResourceVersionHistoryRequest
{
    public TenantScope? TenantScope { get; set; }
    public string? ResourceId { get; set; }
}
```

Result shape:

```csharp
public sealed record ResourceVersionHistoryResult
{
    public TenantScope TenantScope { get; init; } = TenantScope.Default;
    public required string ResourceId { get; init; }
    public IReadOnlyList<ResourceVersionSummary> Versions { get; init; } = [];
}

public sealed record ResourceVersionSummary
{
    public required string ResourceVersionId { get; init; }
    public required int Version { get; init; }
    public required string DefinitionId { get; init; }
    public int? DefinitionVersion { get; init; }
    public required DateTime Created { get; init; }
    public required bool IsLatest { get; init; }
    public required bool IsDraft { get; init; }
    public IReadOnlyList<string> ActiveChannels { get; init; } = [];
    public ResourceLifecycleMarkerState LifecycleState { get; init; } = ResourceLifecycleMarkerState.None;
    public required bool IsProtectedFromPruning { get; init; }
    public required ResourceVersionMaintenanceDisposition MaintenanceDisposition { get; init; }
}

public enum ResourceVersionMaintenanceDisposition
{
    Protected,
    PossibleCandidate,
}
```

## Provider-Facing Contract

Providers expose activation state enumeration through a narrow reader.

```csharp
public interface IResourceActivationStateReader
{
    ValueTask<IReadOnlyList<ActivationState>> ReadActivationStatesAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
```

Provider rules:

- Return activation states only for the supplied tenant.
- Return activation states only for normalized non-empty resource IDs supplied by the caller.
- Return an empty list when no matching activation states exist.
- Preserve the existing activation state payload semantics.
- Do not mutate activation state while reading.

## Validation Rules

History inspection must reject invalid request shape when:

- request is null;
- resource ID is null, empty, or whitespace.

History inspection must not fail when:

- the resource does not exist in the effective tenant;
- the resource has versions but no activation states;
- the resource has no lifecycle marker.

## Ordering Rules

- Version summaries are ordered by `Version` ascending.
- Active channel names are ordered ordinally.
- Results for missing resources contain an empty `Versions` list.
