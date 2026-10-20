using Aster.Core.Models.Instances;

namespace Aster.Core.Models.References;

/// <summary>
/// The outcome of resolving one reference value to a target resource version.
/// </summary>
/// <param name="Status">Whether the reference resolved, and why not when it did not.</param>
/// <param name="TargetResourceId">The referenced <c>ResourceId</c>.</param>
/// <param name="Resource">
/// The resolved target version when <paramref name="Status"/> is <see cref="ResourceReferenceResolutionStatus.Resolved"/>;
/// the target's latest version for <see cref="ResourceReferenceResolutionStatus.TargetTypeMismatch"/>; otherwise <see langword="null"/>.
/// </param>
public sealed record ResourceReferenceResolution(
    ResourceReferenceResolutionStatus Status,
    string TargetResourceId,
    Resource? Resource)
{
    /// <summary>Gets whether the reference resolved to a target version.</summary>
    public bool IsResolved => Status == ResourceReferenceResolutionStatus.Resolved;
}

/// <summary>
/// Outcomes of reference resolution.
/// </summary>
public enum ResourceReferenceResolutionStatus
{
    /// <summary>The reference resolved to a target version.</summary>
    Resolved,

    /// <summary>No resource with the referenced <c>ResourceId</c> exists in the tenant.</summary>
    TargetNotFound,

    /// <summary>The target exists, but its definition is not in the facet's <c>TargetDefinitionIds</c>.</summary>
    TargetTypeMismatch,

    /// <summary>The facet resolves through a channel, and the target has no version active in it.</summary>
    NotActiveInChannel,
}
