using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Lifecycle;

/// <summary>
/// Shared lifecycle context data.
/// </summary>
public abstract record ResourceLifecycleContext
{
    /// <summary>
    /// Tenant scope used by the underlying operation.
    /// </summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>
    /// Per-operation identifier shared by before and after hooks.
    /// </summary>
    public required Guid OperationId { get; init; }

    /// <summary>
    /// Lifecycle point being invoked.
    /// </summary>
    public required LifecyclePoint LifecyclePoint { get; init; }

    /// <summary>
    /// Cancellation token for the underlying operation.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Lifecycle context for resource save operations.
/// </summary>
public sealed record ResourceSaveLifecycleContext : ResourceLifecycleContext
{
    /// <summary>
    /// Save operation kind.
    /// </summary>
    public required ResourceSaveKind SaveKind { get; init; }

    /// <summary>
    /// Logical definition identifier.
    /// </summary>
    public required string DefinitionId { get; init; }

    /// <summary>
    /// Logical resource identifier when known.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Optimistic concurrency base version when applicable.
    /// </summary>
    public int? BaseVersion { get; init; }

    /// <summary>
    /// Proposed or saved resource version.
    /// </summary>
    public Resource? Resource { get; init; }
}

/// <summary>
/// Lifecycle context for activation and deactivation operations.
/// </summary>
public sealed record ResourceActivationLifecycleContext : ResourceLifecycleContext
{
    /// <summary>
    /// Logical resource identifier.
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Resource version being activated or deactivated.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Activation channel.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Whether activation permits multiple active versions.
    /// </summary>
    public bool AllowMultipleActive { get; init; }

    /// <summary>
    /// Resulting active versions for the channel.
    /// </summary>
    public IReadOnlyList<int> ActiveVersions { get; init; } = [];
}

/// <summary>
/// Lifecycle context for portable snapshot export operations.
/// </summary>
public sealed record ResourceExportLifecycleContext : ResourceLifecycleContext
{
    /// <summary>
    /// Export request.
    /// </summary>
    public required PortableSnapshotExportRequest ExportRequest { get; init; }

    /// <summary>
    /// Exported snapshot when available.
    /// </summary>
    public PortableSnapshot? Snapshot { get; init; }

    /// <summary>
    /// Export result when available.
    /// </summary>
    public PortableSnapshotExportResult? ExportResult { get; init; }
}

/// <summary>
/// Lifecycle context for portable snapshot preview and import operations.
/// </summary>
public sealed record ResourceImportLifecycleContext : ResourceLifecycleContext
{
    /// <summary>
    /// Portable snapshot being previewed or imported.
    /// </summary>
    public required PortableSnapshot Snapshot { get; init; }

    /// <summary>
    /// Import options.
    /// </summary>
    public required PortableImportOptions ImportOptions { get; init; }

    /// <summary>
    /// Import preview result when available.
    /// </summary>
    public PortableImportPreview? Preview { get; init; }

    /// <summary>
    /// Import result when available.
    /// </summary>
    public PortableImportResult? ImportResult { get; init; }
}
