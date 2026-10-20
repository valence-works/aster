using Aster.Core.Models.Querying;

namespace Aster.Core.Extensions;

/// <summary>
/// Forward reference filters: resources whose reference facet names a given target.
/// </summary>
public static class ReferenceQuery
{
    /// <summary>
    /// Creates a filter matching resources whose <c>reference</c> facet equals, or whose
    /// <c>reference[]</c> facet contains, <paramref name="targetResourceId"/>. Queries are
    /// definition-agnostic (ADR 0002 §9), so the filter covers both shapes; each operator simply does
    /// not match the other shape.
    /// </summary>
    /// <param name="aspectKey">The aspect attachment key that holds the facet.</param>
    /// <param name="facetDefinitionId">The reference facet's ID.</param>
    /// <param name="targetResourceId">The referenced <c>ResourceId</c>.</param>
    /// <returns>A portable filter expression.</returns>
    /// <remarks>
    /// Providers compare text case-insensitively, so <c>ResourceId</c>s that differ only in case both match.
    /// <see cref="Abstractions.IResourceReferenceService"/> confirms matches ordinally.
    /// </remarks>
    public static FilterExpression Targets(string aspectKey, string facetDefinitionId, string targetResourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(facetDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);

        return new LogicalExpression(LogicalOperator.Or,
        [
            new FacetValueFilter(aspectKey, facetDefinitionId, targetResourceId, ComparisonOperator.Equals),
            new FacetValueFilter(aspectKey, facetDefinitionId, targetResourceId, ComparisonOperator.ArrayContains),
        ]);
    }
}
