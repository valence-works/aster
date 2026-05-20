# Contract: Portability Primitives

## Public SDK Behavior

The SDK exposes an explicit portability workflow:

1. Export definitions, resources, resource versions, and activation state into a portable snapshot.
2. Validate a snapshot before writing it.
3. Preview import to see planned counts, identity mappings, and diagnostics without mutation.
4. Write import as an all-or-nothing operation.

The workflow MUST NOT introduce recipes, lifecycle hooks, live synchronization, runtime scanning, provider registries, migration engines, public SQL, or public `IQueryable<Resource>`.

## Service Contract

```csharp
public interface IResourcePortabilityService
{
    ValueTask<PortableSnapshotExportResult> ExportAsync(
        PortableSnapshotExportRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PortableSnapshotValidationResult> ValidateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<PortableImportPreview> PreviewImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<PortableImportResult> ImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

## Export Contract

```csharp
public sealed class PortableSnapshotExportRequest
{
    public PortableExportScopeMode ScopeMode { get; set; }
    public HashSet<string> DefinitionIds { get; set; } = [];
    public HashSet<string> ResourceIds { get; set; } = [];
    public PortableResourceVersionScope ResourceVersionScope { get; set; }
    public HashSet<ResourceVersionReference> SpecificResourceVersions { get; set; } = [];
}

public enum PortableExportScopeMode
{
    DefinitionsOnly,
    SelectedResources,
    DefinitionWithResources,
}

public enum PortableResourceVersionScope
{
    AllVersions,
    LatestOnly,
    SpecificVersions,
}
```

Rules:

- Export scope is explicit; no implicit whole-store export.
- Exporting resources includes referenced definition versions.
- Activation entries are included only when their referenced resource versions are exported.
- Skipped activation entries are reported as diagnostics.

## Snapshot Contract

```csharp
public sealed record PortableSnapshot
{
    public required int FormatVersion { get; init; }
    public IReadOnlyList<ResourceDefinition> Definitions { get; init; } = [];
    public IReadOnlyList<Resource> Resources { get; init; } = [];
    public IReadOnlyList<ActivationState> ActivationStates { get; init; } = [];
}
```

Rules:

- Initial supported `FormatVersion` is `1`.
- Snapshot identity is based on SDK model identifiers and version numbers.
- A snapshot may contain warnings, diagnostics, or skipped activation metadata in result objects; imported snapshot content is limited to definitions, resources, and activation state.

## Import Contract

```csharp
public sealed class PortableImportOptions
{
    public PortableImportCollisionMode CollisionMode { get; set; } = PortableImportCollisionMode.Strict;
}

public enum PortableImportCollisionMode
{
    Strict,
    RemapDivergent,
}
```

Rules:

- Strict mode is the default.
- Identical existing content is reused/no-op.
- Divergent existing content is a collision.
- Strict mode fails before writing when divergent collisions exist.
- Remap mode maps divergent collisions to deterministic replacement identifiers.
- Preview import is non-mutating.
- Write import is all-or-nothing.

## Result And Diagnostic Contract

```csharp
public sealed record PortableDiagnostic
{
    public required string Code { get; init; }
    public required PortableDiagnosticSeverity Severity { get; init; }
    public string? Path { get; init; }
    public required string Message { get; init; }
}

public enum PortableDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record PortableIdentityMapping
{
    public required string EntityKind { get; init; }
    public required string OriginalId { get; init; }
    public required string ImportedId { get; init; }
    public required string Reason { get; init; }
}
```

Required diagnostic codes include:

- `missing-definition`
- `missing-definition-version`
- `missing-resource`
- `missing-resource-version`
- `duplicate-snapshot-identity`
- `unresolved-definition-reference`
- `unresolved-resource-reference`
- `divergent-identity-collision`
- `unsupported-snapshot-format`
- `malformed-snapshot`
- `skipped-activation-entry`

## Provider-Facing Store Contract

The implementation may introduce a narrow provider-facing contract for exact portability reads and atomic writes:

```csharp
public interface IResourcePortabilityStore
{
    ValueTask<PortableStoreSnapshot> ReadSnapshotAsync(
        PortableStoreReadRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PortableTargetState> ReadTargetStateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask ApplyImportAsync(
        PortableSnapshot plannedSnapshot,
        CancellationToken cancellationToken = default);
}
```

Rules:

- This contract is provider infrastructure, not provider discovery.
- It exists because lifecycle APIs do not expose all activation channels or exact definition-version writes.
- Providers own atomic apply behavior.
