using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;

namespace Aster.Core.Models.Portability;

/// <summary>
/// Provider-facing snapshot read request.
/// </summary>
public sealed record PortableStoreReadRequest
{
    /// <summary>
    /// Validated export request.
    /// </summary>
    public required PortableSnapshotExportRequest ExportRequest { get; init; }
}

/// <summary>
/// Provider-facing snapshot read result.
/// </summary>
public sealed record PortableStoreSnapshot
{
    /// <summary>
    /// Definition versions read from storage.
    /// </summary>
    public IReadOnlyList<ResourceDefinition> Definitions { get; init; } = [];

    /// <summary>
    /// Resource versions read from storage.
    /// </summary>
    public IReadOnlyList<Resource> Resources { get; init; } = [];

    /// <summary>
    /// Activation states whose referenced resource versions are present.
    /// </summary>
    public IReadOnlyList<ActivationState> ActivationStates { get; init; } = [];

    /// <summary>
    /// Lifecycle markers whose referenced resources are present.
    /// </summary>
    public IReadOnlyList<ResourceLifecycleMarker> LifecycleMarkers { get; init; } = [];

    /// <summary>
    /// Activation entries skipped because referenced resource versions were excluded.
    /// </summary>
    public IReadOnlyList<SkippedActivationEntry> SkippedActivationEntries { get; init; } = [];
}

/// <summary>
/// Provider-facing target state for import planning.
/// </summary>
public sealed record PortableTargetState
{
    /// <summary>
    /// Existing definition versions.
    /// </summary>
    public IReadOnlyList<ResourceDefinition> Definitions { get; init; } = [];

    /// <summary>
    /// Existing resource versions.
    /// </summary>
    public IReadOnlyList<Resource> Resources { get; init; } = [];

    /// <summary>
    /// Existing activation states.
    /// </summary>
    public IReadOnlyList<ActivationState> ActivationStates { get; init; } = [];

    /// <summary>
    /// Existing lifecycle markers.
    /// </summary>
    public IReadOnlyList<ResourceLifecycleMarker> LifecycleMarkers { get; init; } = [];
}

/// <summary>
/// Reason an activation entry was skipped during export.
/// </summary>
public enum SkippedActivationReason
{
    /// <summary>
    /// Activation references a resource version excluded by the export scope.
    /// </summary>
    ExcludedByResourceVersionScope,
}

/// <summary>
/// Activation entry skipped during export.
/// </summary>
public sealed record SkippedActivationEntry
{
    /// <summary>
    /// Logical resource identifier.
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Activation channel.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Resource version referenced by the activation entry.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Skip reason.
    /// </summary>
    public required SkippedActivationReason Reason { get; init; }
}
