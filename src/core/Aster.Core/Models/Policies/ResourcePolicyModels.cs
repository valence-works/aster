using Aster.Core.Models.Instances;

namespace Aster.Core.Models.Policies;

/// <summary>
/// Declarative policy intent attached to a resource definition.
/// </summary>
public sealed record ResourcePolicyDeclaration
{
    /// <summary>Stable identifier unique within the declaring definition.</summary>
    public required string PolicyId { get; init; }

    /// <summary>Host-visible policy name.</summary>
    public string? Name { get; init; }

    /// <summary>Policy category.</summary>
    public required ResourcePolicyKind Kind { get; init; }

    /// <summary>Policy target type.</summary>
    public required ResourcePolicyTarget Target { get; init; }

    /// <summary>Policy outcome intent.</summary>
    public required ResourcePolicyOutcome Outcome { get; init; }

    /// <summary>Explicit criteria supported by this policy slice.</summary>
    public ResourcePolicyCriteria Criteria { get; init; } = new();
}

/// <summary>
/// Supported explicit criteria for resource policies.
/// </summary>
public sealed record ResourcePolicyCriteria
{
    /// <summary>Minimum age relative to the host-supplied evaluation timestamp.</summary>
    public TimeSpan? MinimumAge { get; init; }

    /// <summary>Maximum number of resource versions to retain during pruning preview.</summary>
    public int? MaximumRetainedVersions { get; init; }

    /// <summary>Optional activation-state criterion.</summary>
    public ResourcePolicyActivationState? ActivationState { get; init; }

    /// <summary>Activation channel used when <see cref="ActivationState"/> is <see cref="ResourcePolicyActivationState.Active"/>.</summary>
    public string? ActivationChannel { get; init; }

    /// <summary>Optional lifecycle marker criterion.</summary>
    public ResourceLifecycleMarkerState? LifecycleState { get; init; }

    /// <summary>
    /// Sentinel for unsupported facet predicates. This keeps validation explicit until a future policy criteria model is specified.
    /// </summary>
    public string? UnsupportedFacetPredicate { get; init; }
}

/// <summary>
/// Supported policy kinds.
/// </summary>
public enum ResourcePolicyKind
{
    /// <summary>Retention intent.</summary>
    Retention,

    /// <summary>Archive intent.</summary>
    Archival,

    /// <summary>Soft-delete intent.</summary>
    SoftDelete,

    /// <summary>Version pruning intent.</summary>
    VersionPruning,
}

/// <summary>
/// Supported policy target shapes.
/// </summary>
public enum ResourcePolicyTarget
{
    /// <summary>The policy targets logical resources.</summary>
    Resource,

    /// <summary>The policy targets individual resource versions.</summary>
    ResourceVersion,
}

/// <summary>
/// Supported policy outcome intents.
/// </summary>
public enum ResourcePolicyOutcome
{
    /// <summary>Retain matching data.</summary>
    Retain,

    /// <summary>Archive matching resources through explicit marker writes.</summary>
    Archive,

    /// <summary>Soft-delete matching resources through explicit marker writes.</summary>
    SoftDelete,

    /// <summary>Report versions that would be pruned; destructive pruning is out of scope.</summary>
    PrunePreview,
}

/// <summary>
/// Activation-state criteria supported by policy preview.
/// </summary>
public enum ResourcePolicyActivationState
{
    /// <summary>Resource version is active in the requested activation channel.</summary>
    Active,

    /// <summary>Resource version is not active in any channel.</summary>
    Draft,
}
