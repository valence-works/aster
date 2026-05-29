# Contract: Historical Version Activation

Historical version activation changes the semantics of existing activation APIs from "latest only" to "any existing version".

## Public SDK Behavior

Existing host-facing methods remain the API:

```csharp
ValueTask ActivateAsync(
    string resourceId,
    int version,
    string channel,
    bool allowMultipleActive = false,
    CancellationToken cancellationToken = default);

ValueTask ActivateAsync(
    string resourceId,
    int version,
    string channel,
    TenantScope tenantScope,
    bool allowMultipleActive,
    CancellationToken cancellationToken);
```

Rules:

- `resourceId` and `channel` must be non-blank.
- `version` must exist for the resource in the effective tenant.
- Non-latest versions are valid activation targets.
- Activating a historical version does not create a new resource version.
- Activating a historical version does not change latest.
- `allowMultipleActive = false` replaces the active version set for the channel with the requested version.
- `allowMultipleActive = true` appends the requested version to the channel's active version set.
- Active version sets remain ordered.
- Existing activation hooks run.

## Provider Behavior

No new provider contract is introduced. Providers continue to persist activation state through `IResourceVersionWriter.UpdateActivationAsync`.

Providers must not:

- rewrite resource versions;
- change latest;
- mutate lifecycle marker state;
- introduce schema changes for this behavior.

## Failure Behavior

Historical activation must fail when:

- resource ID is blank;
- channel is blank;
- requested version does not exist in the effective tenant;
- lifecycle hooks reject or fail activation according to existing hook rules.

Historical activation must not fail merely because:

- requested version is not latest;
- newer versions exist;
- another version is already active in the channel.
