# Contract: Definition Schema Versions & Upgrade Flow

## Public SDK Behavior

The SDK exposes an explicit schema-version workflow for resource versions:

1. Inspect the schema status for one resource version.
2. Explicitly upgrade the latest resource version to an existing target definition version.
3. Receive either an upgraded resource version, a no-op result, or structured failure diagnostics.

The workflow MUST NOT automatically rewrite historical resource versions.

## Schema Status Contract

```csharp
public interface IResourceSchemaVersionService
{
    ValueTask<ResourceSchemaStatusResult> GetSchemaStatusAsync(
        Resource resource,
        CancellationToken cancellationToken = default);

    ValueTask<ResourceSchemaUpgradeResult> UpgradeAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken = default);
}
```

`GetSchemaStatusAsync` evaluates exactly one resource version snapshot. It does not inspect every version of a resource and does not mutate state.

## Status Result Contract

```csharp
public enum ResourceSchemaStatus
{
    Current,
    OlderThanLatest,
    MissingDefinition,
    MissingDefinitionVersion,
    UnknownResourceLineage,
}

public sealed record ResourceSchemaStatusResult
{
    public required string ResourceId { get; init; }
    public required int ResourceVersion { get; init; }
    public required string DefinitionId { get; init; }
    public int? RecordedDefinitionVersion { get; init; }
    public int? LatestDefinitionVersion { get; init; }
    public required ResourceSchemaStatus Status { get; init; }
    public required string Message { get; init; }
}
```

## Upgrade Request Contract

```csharp
public sealed class ResourceSchemaUpgradeRequest
{
    public int BaseVersion { get; set; }
    public int? TargetDefinitionVersion { get; set; }
    public Dictionary<string, object> AspectUpdates { get; set; } = [];
}
```

Rules:

- `BaseVersion` is the existing optimistic concurrency token.
- `TargetDefinitionVersion` defaults to latest when omitted.
- `AspectUpdates` uses the same state-replace semantics as normal resource updates.
- Unknown-lineage resources may be upgraded when the target definition version exists.

## Upgrade Result Contract

```csharp
public enum ResourceSchemaUpgradeStatus
{
    Upgraded,
    NoOp,
}

public sealed record ResourceSchemaUpgradeResult
{
    public required ResourceSchemaUpgradeStatus Status { get; init; }
    public Resource? Resource { get; init; }
    public int? SourceDefinitionVersion { get; init; }
    public required int TargetDefinitionVersion { get; init; }
    public IReadOnlyList<string> CarriedForwardAspectKeys { get; init; } = [];
    public required string Message { get; init; }
}
```

Rules:

- `Upgraded` means a new immutable resource version was appended.
- `NoOp` means the requested target equals the source definition version and no new resource version was appended.
- `CarriedForwardAspectKeys` lists source aspect keys not declared by the target definition version and preserved by default.

## Failure Contract

Failures use structured SDK exceptions consistent with existing lifecycle behavior.

Required failure cases:

- Missing definition: `ResourceSchemaUpgradeException` with code `missing-definition`.
- Missing target definition version: `ResourceSchemaUpgradeException` with code `missing-definition-version`.
- Target definition version older than the source lineage: `ResourceSchemaUpgradeException` with code `target-definition-version-before-source`.
- Target definition version newer than latest known definition version: `ResourceSchemaUpgradeException` with code `target-definition-version-too-new`.
- Stale `BaseVersion`: existing `ConcurrencyException`.
- Missing latest resource version: existing `VersionNotFoundException`.

## Non-Goals

- No automatic migration runner.
- No background resource rewriting.
- No transformation pipeline.
- No provider registry.
- No runtime scanning.
- No query planner.
- No public SQL or public `IQueryable<Resource>`.
