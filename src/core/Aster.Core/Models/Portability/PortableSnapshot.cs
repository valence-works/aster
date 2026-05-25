using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Portability;

/// <summary>
/// Self-contained portable representation of selected Aster data.
/// </summary>
public sealed record PortableSnapshot
{
    /// <summary>
    /// Initial supported snapshot format version.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Snapshot format version.
    /// </summary>
    public required int FormatVersion { get; init; }

    /// <summary>
    /// Source tenant scope represented by the snapshot.
    /// </summary>
    public TenantScope SourceTenantScope { get; init; } = TenantScope.Default;

    /// <summary>
    /// Definition versions included in the snapshot.
    /// </summary>
    public IReadOnlyList<ResourceDefinition> Definitions { get; init; } = [];

    /// <summary>
    /// Resource versions included in the snapshot.
    /// </summary>
    public IReadOnlyList<Resource> Resources { get; init; } = [];

    /// <summary>
    /// Activation state entries whose referenced resource versions are included.
    /// </summary>
    public IReadOnlyList<ActivationState> ActivationStates { get; init; } = [];

    /// <summary>
    /// Lifecycle markers whose referenced resources are included.
    /// </summary>
    public IReadOnlyList<ResourceLifecycleMarker> LifecycleMarkers { get; init; } = [];
}

/// <summary>
/// Explicit export request.
/// </summary>
public sealed class PortableSnapshotExportRequest
{
    /// <summary>
    /// Tenant scope to export. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>
    /// Requested export scope.
    /// </summary>
    public PortableExportScopeMode ScopeMode { get; set; }

    /// <summary>
    /// Definition IDs used by definition-scoped export modes.
    /// </summary>
    public HashSet<string> DefinitionIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Resource IDs used by selected-resource export mode.
    /// </summary>
    public HashSet<string> ResourceIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Resource version selection behavior.
    /// </summary>
    public PortableResourceVersionScope ResourceVersionScope { get; set; } = PortableResourceVersionScope.AllVersions;

    /// <summary>
    /// Specific resource versions used when <see cref="ResourceVersionScope"/> is <see cref="PortableResourceVersionScope.SpecificVersions"/>.
    /// </summary>
    public HashSet<ResourceVersionReference> SpecificResourceVersions { get; set; } = [];
}

/// <summary>
/// Reference to one resource version.
/// </summary>
public sealed record ResourceVersionReference
{
    /// <summary>
    /// Logical resource identifier.
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Resource version number.
    /// </summary>
    public required int Version { get; init; }
}

/// <summary>
/// Export scope mode.
/// </summary>
public enum PortableExportScopeMode
{
    /// <summary>
    /// Export selected definition versions only.
    /// </summary>
    DefinitionsOnly,

    /// <summary>
    /// Export selected resources.
    /// </summary>
    SelectedResources,

    /// <summary>
    /// Export selected definitions and resources using those definitions.
    /// </summary>
    DefinitionWithResources,
}

/// <summary>
/// Resource version selection behavior for export.
/// </summary>
public enum PortableResourceVersionScope
{
    /// <summary>
    /// Include every selected resource version.
    /// </summary>
    AllVersions,

    /// <summary>
    /// Include only the latest selected resource version.
    /// </summary>
    LatestOnly,

    /// <summary>
    /// Include only explicitly named resource versions.
    /// </summary>
    SpecificVersions,
}
