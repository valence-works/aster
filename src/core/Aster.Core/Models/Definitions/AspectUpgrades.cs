using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Definitions;

/// <summary>
/// Which resource definitions pin an older version of a shared aspect, and what moving them to the
/// target version would change (ADR 0003 §6).
/// </summary>
public sealed record AspectUpgradePreview
{
    /// <summary>Gets the tenant the preview was computed in.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Gets the shared aspect definition ID.</summary>
    public required string AspectDefinitionId { get; init; }

    /// <summary>Gets the target version; <see langword="null"/> when it does not exist.</summary>
    public int? TargetVersion { get; init; }

    /// <summary>
    /// Gets one candidate per attachment of an older version on a resource definition's latest version,
    /// ordered by definition ID, then attachment key (ordinal).
    /// </summary>
    public IReadOnlyList<AspectUpgradeCandidate> Candidates { get; init; } = [];

    /// <summary>Gets diagnostics, such as a missing target version.</summary>
    public IReadOnlyList<SharedAspectDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// An attachment that pins an older version of the shared aspect.
/// </summary>
/// <param name="DefinitionId">The resource definition ID.</param>
/// <param name="DefinitionVersion">The resource definition's latest version, which holds the pin.</param>
/// <param name="AttachmentKey">The attachment key.</param>
/// <param name="PinnedVersion">The pinned aspect version.</param>
/// <param name="Changes">Facet differences from the pinned to the target version, ordered by facet ID, then kind.</param>
public sealed record AspectUpgradeCandidate(
    string DefinitionId,
    int DefinitionVersion,
    string AttachmentKey,
    int PinnedVersion,
    IReadOnlyList<AspectFacetChange> Changes);

/// <summary>
/// One facet difference between two aspect versions.
/// </summary>
/// <param name="FacetDefinitionId">The facet ID.</param>
/// <param name="Kind">The kind of change.</param>
/// <param name="From">The previous value (type, required flag, or serialized metadata); <see langword="null"/> when added.</param>
/// <param name="To">The new value; <see langword="null"/> when removed.</param>
public sealed record AspectFacetChange(string FacetDefinitionId, AspectFacetChangeKind Kind, string? From, string? To);

/// <summary>
/// Kinds of facet differences between aspect versions.
/// </summary>
public enum AspectFacetChangeKind
{
    /// <summary>The facet exists only in the target version.</summary>
    Added,

    /// <summary>The facet exists only in the pinned version.</summary>
    Removed,

    /// <summary>The facet's type descriptor changed.</summary>
    TypeChanged,

    /// <summary>The facet's <c>IsRequired</c> flag changed.</summary>
    RequiredChanged,

    /// <summary>The facet's metadata (constraints, UI hints, references) changed.</summary>
    MetadataChanged,
}

/// <summary>
/// Selection for <see cref="Abstractions.IAspectUpgradeService.ApplyAsync"/>.
/// </summary>
public sealed record AspectUpgradeRequest
{
    /// <summary>Gets the tenant; the default tenant when <see langword="null"/>.</summary>
    public TenantScope? TenantScope { get; init; }

    /// <summary>Gets the shared aspect definition ID.</summary>
    public required string AspectDefinitionId { get; init; }

    /// <summary>Gets the target version; <see langword="null"/> means the latest.</summary>
    public int? TargetVersion { get; init; }

    /// <summary>Gets the resource definitions to re-pin; only these get new versions.</summary>
    public IReadOnlyList<string> DefinitionIds { get; init; } = [];
}

/// <summary>
/// The outcome of re-pinning selected resource definitions.
/// </summary>
public sealed record AspectUpgradeResult
{
    /// <summary>Gets the tenant.</summary>
    public TenantScope TenantScope { get; init; } = TenantScope.Default;

    /// <summary>Gets the target version; <see langword="null"/> when it does not exist.</summary>
    public int? TargetVersion { get; init; }

    /// <summary>Gets one outcome per distinct selected definition, in selection order.</summary>
    public IReadOnlyList<AspectUpgradeOutcome> Outcomes { get; init; } = [];

    /// <summary>Gets request-level diagnostics, such as a missing target version.</summary>
    public IReadOnlyList<SharedAspectDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// The outcome for one selected resource definition.
/// </summary>
/// <param name="DefinitionId">The resource definition ID.</param>
/// <param name="Status">Whether a new definition version was registered.</param>
/// <param name="NewDefinitionVersion">The registered definition version when <see cref="AspectUpgradeStatus.Upgraded"/>.</param>
/// <param name="AttachmentKeys">The re-pinned attachment keys, ordered (ordinal).</param>
/// <param name="Diagnostics">Why the definition was not upgraded, when it was not.</param>
public sealed record AspectUpgradeOutcome(
    string DefinitionId,
    AspectUpgradeStatus Status,
    int? NewDefinitionVersion,
    IReadOnlyList<string> AttachmentKeys,
    IReadOnlyList<SharedAspectDiagnostic> Diagnostics);

/// <summary>
/// Per-definition upgrade status.
/// </summary>
public enum AspectUpgradeStatus
{
    /// <summary>A new definition version pins the target aspect version.</summary>
    Upgraded,

    /// <summary>The definition does not pin an older version of the aspect; nothing was written.</summary>
    NotApplicable,

    /// <summary>The definition could not be upgraded; nothing was written.</summary>
    Failed,
}
