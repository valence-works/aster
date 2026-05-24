using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Instances;

/// <summary>
/// Request to explicitly upgrade a resource to a target definition version.
/// </summary>
public sealed class ResourceSchemaUpgradeRequest
{
    /// <summary>
    /// Tenant scope for the schema upgrade. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Must match the current latest resource version.
    /// </summary>
    public int BaseVersion { get; set; }

    /// <summary>
    /// Optional target definition version. When omitted, the latest definition version is used.
    /// </summary>
    public int? TargetDefinitionVersion { get; set; }

    /// <summary>
    /// Aspect updates keyed by aspect key. State replace semantics match normal resource updates.
    /// </summary>
    public Dictionary<string, object> AspectUpdates { get; set; } = [];
}

/// <summary>
/// Outcome kind for an explicit schema upgrade request.
/// </summary>
public enum ResourceSchemaUpgradeStatus
{
    /// <summary>A new resource version was appended.</summary>
    Upgraded,

    /// <summary>No resource version was appended because the target matches the source lineage.</summary>
    NoOp,
}

/// <summary>
/// Result of an explicit schema upgrade request.
/// </summary>
public sealed record ResourceSchemaUpgradeResult
{
    /// <summary>Gets the upgrade outcome kind.</summary>
    public required ResourceSchemaUpgradeStatus Status { get; init; }

    /// <summary>Gets the upgraded resource version when one was created.</summary>
    public Resource? Resource { get; init; }

    /// <summary>Gets the source definition version, when known.</summary>
    public int? SourceDefinitionVersion { get; init; }

    /// <summary>Gets the requested target definition version.</summary>
    public required int TargetDefinitionVersion { get; init; }

    /// <summary>Gets aspect keys preserved even though the target definition does not declare them.</summary>
    public IReadOnlyList<string> CarriedForwardAspectKeys { get; init; } = [];

    /// <summary>Gets a human-readable result message.</summary>
    public required string Message { get; init; }
}
