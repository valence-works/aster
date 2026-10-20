using Aster.Core.Models.References;

namespace Aster.Core.Abstractions;

/// <summary>
/// Reverse reference lookup: finds the resources whose reference facets name a target resource
/// (ADR 0002 §6, #88), for questions like "which workflows use agent X?" before changing or archiving it.
/// </summary>
public interface IResourceReferenceService
{
    /// <summary>
    /// Returns the resources, within one tenant, that reference <paramref name="targetResourceId"/> from a
    /// <c>reference</c> or <c>reference[]</c> facet declared by their definition's latest version.
    /// </summary>
    /// <param name="targetResourceId">The referenced <c>ResourceId</c>; matched ordinally.</param>
    /// <param name="query">Scope, filtering and paging options; latest versions in the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One page of referrers, ordered by <c>ResourceId</c>.</returns>
    /// <exception cref="NotSupportedException">The active query provider does not declare <c>SupportsReverseReferenceLookup</c>.</exception>
    ValueTask<ResourceReferrerPage> GetReferrersAsync(
        string targetResourceId,
        ResourceReferrerQuery? query = null,
        CancellationToken cancellationToken = default);
}
