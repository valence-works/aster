using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.References;

/// <summary>
/// Options for <see cref="Abstractions.IResourceReferenceService.GetReferrersAsync"/>.
/// </summary>
public sealed record ResourceReferrerQuery
{
    /// <summary>Gets the tenant to search; the default tenant when <see langword="null"/>.</summary>
    public TenantScope? TenantScope { get; init; }

    /// <summary>Gets which referrer versions to search. Defaults to latest versions.</summary>
    public ResourceVersionScope Scope { get; init; } = ResourceVersionScope.Latest;

    /// <summary>Gets the activation channel; required when <see cref="Scope"/> is <see cref="ResourceVersionScope.Active"/>.</summary>
    public string? ActivationChannel { get; init; }

    /// <summary>Gets an optional referrer definition ID; when set, only resources of that definition are searched.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>Gets an optional lifecycle marker state filter for referrers; when omitted, markers do not affect results.</summary>
    public ResourceLifecycleMarkerState? LifecycleState { get; init; }

    /// <summary>Gets the number of referrers to skip, in <c>ResourceId</c> order.</summary>
    public int? Skip { get; init; }

    /// <summary>
    /// Gets the maximum number of referrers to return; <see langword="null"/> returns all. Paging runs in the
    /// provider, which compares text case-insensitively, so a page can hold fewer items when another
    /// <c>ResourceId</c> differs from the target only by letter case.
    /// </summary>
    public int? Take { get; init; }
}

/// <summary>
/// One page of resources that reference a target resource.
/// </summary>
/// <param name="Items">The referrers, ordered by <c>ResourceId</c> (ordinal).</param>
/// <param name="HasMore">Whether further referrers exist after this page.</param>
public sealed record ResourceReferrerPage(IReadOnlyList<ResourceReferrer> Items, bool HasMore);

/// <summary>
/// A resource version that references the target, with every place it does so.
/// </summary>
/// <param name="Resource">The referring resource version.</param>
/// <param name="Locations">Where the version holds the reference, ordered by aspect key, facet ID, then element index.</param>
public sealed record ResourceReferrer(Resource Resource, IReadOnlyList<ResourceReferenceLocation> Locations);

/// <summary>
/// A reference facet value that names the target.
/// </summary>
/// <param name="AspectKey">The aspect attachment key.</param>
/// <param name="FacetDefinitionId">The reference facet's ID.</param>
/// <param name="ElementIndex">The element index for <c>reference[]</c> facets; <see langword="null"/> for <c>reference</c>.</param>
public sealed record ResourceReferenceLocation(string AspectKey, string FacetDefinitionId, int? ElementIndex);
