# Contract: Policy Pruning Application

Policy pruning application provides host-controlled, tenant-scoped destructive removal for selected version-pruning preview outcomes.

## Host-Facing Behavior

Hosts can:

- submit selected pruning candidates derived from policy preview outcomes;
- preview operator impact by inspecting deterministic per-candidate application results;
- retry requests safely when candidates were already pruned;
- distinguish invalid, stale, protected, unsupported, and write-failed candidates through stable diagnostics.

The SDK must:

- resolve one effective tenant per request;
- preserve candidate input order in results;
- return one result for every input candidate;
- allow partial success for unrelated candidates;
- protect latest and active versions;
- revalidate current policy and criteria before removal;
- preserve all non-target state.

The SDK must not:

- run pruning automatically;
- remove versions not explicitly submitted by the host;
- remove versions outside the effective tenant;
- rewrite remaining versions, activation state, lifecycle markers, definitions, or policy declarations;
- add public SQL, public queryable resource surfaces, schedulers, provider registries, or authorization behavior.

## Proposed Public SDK Contract

```csharp
public interface IResourcePolicyPruningApplicationService
{
    ValueTask<ResourcePolicyPruningApplicationResult> ApplyAsync(
        ResourcePolicyPruningApplicationRequest request,
        CancellationToken cancellationToken = default);
}
```

Request shape:

```csharp
public sealed class ResourcePolicyPruningApplicationRequest
{
    public TenantScope? TenantScope { get; set; }
    public List<ResourcePolicyPruningApplicationCandidate> Candidates { get; set; } = [];
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class ResourcePolicyPruningApplicationCandidate
{
    public string? PolicyId { get; set; }
    public ResourcePolicyKind? PolicyKind { get; set; }
    public ResourcePolicyOutcome? Outcome { get; set; }
    public string? ResourceId { get; set; }
    public int? ResourceVersion { get; set; }
}
```

Result shape:

```csharp
public sealed record ResourcePolicyPruningApplicationResult
{
    public TenantScope TenantScope { get; init; } = TenantScope.Default;
    public DateTimeOffset? AppliedAt { get; init; }
    public IReadOnlyList<ResourcePolicyPruningApplicationCandidateResult> Candidates { get; init; } = [];
    public int PrunedCount { get; }
    public int AlreadyPrunedCount { get; }
    public int SkippedCount { get; }
    public int FailedCount { get; }
}

public sealed record ResourcePolicyPruningApplicationCandidateResult
{
    public required int Index { get; init; }
    public required ResourcePolicyPruningApplicationCandidateStatus Status { get; init; }
    public string? PolicyId { get; init; }
    public string? ResourceId { get; init; }
    public int? ResourceVersion { get; init; }
    public IReadOnlyList<ResourcePolicyDiagnostic> Diagnostics { get; init; } = [];
}

public enum ResourcePolicyPruningApplicationCandidateStatus
{
    Pruned,
    AlreadyPruned,
    Skipped,
    Failed,
}
```

## Provider-Facing Contract

Providers that support destructive version pruning implement a narrow version removal capability.

```csharp
public interface IResourceVersionPruningStore
{
    ValueTask<bool> PruneVersionAsync(
        string resourceId,
        int resourceVersion,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
```

Provider rules:

- `PruneVersionAsync` must remove only the matching resource version in the supplied tenant.
- `PruneVersionAsync` must return `true` only when a matching version existed and was removed.
- `PruneVersionAsync` must return `false` when no matching version exists.
- Providers must not remove activation state, lifecycle marker state, definitions, policy declarations, or other versions through this operation.
- Providers should implement removal with current-state conditions where possible.
- Core registration should provide an unsupported fallback so hosts using providers without pruning support receive `policy-pruning-provider-unsupported` results instead of service resolution failures.

## Validation Rules

Application preflight must reject candidates when:

- candidate shape is invalid;
- policy kind is not version pruning;
- outcome is not prune-preview;
- target resource is missing in the effective tenant;
- target version is latest;
- target version is active in any channel;
- current policy declaration is missing;
- current policy declaration no longer matches the submitted kind or outcome;
- target version no longer satisfies current policy criteria;
- removal would violate the retained-version safety floor;
- provider pruning capability is unavailable.

Already-pruned behavior:

- If the target resource exists and the submitted version is absent, the candidate is `AlreadyPruned` when policy identity and tenant scope can still be validated.
- If the target resource is missing, the candidate is `Failed` with target-not-found diagnostics.

Duplicate behavior:

- First valid occurrence for a `(resourceId, resourceVersion)` pair determines the write outcome.
- Later duplicates are `Skipped` when the first occurrence pruned or failed.
- Later duplicates are `AlreadyPruned` when the first occurrence was already pruned.

## Diagnostics

Stable diagnostic codes added by this slice:

- `policy-pruning-candidate-invalid`
- `policy-pruning-target-not-found`
- `policy-pruning-version-protected-latest`
- `policy-pruning-version-protected-active`
- `policy-pruning-policy-missing`
- `policy-pruning-policy-mismatch`
- `policy-pruning-unsafe`
- `policy-pruning-provider-unsupported`
- `policy-pruning-write-failed`

Existing diagnostics remain unchanged.
