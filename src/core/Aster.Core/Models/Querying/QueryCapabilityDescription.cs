namespace Aster.Core.Models.Querying;

/// <summary>
/// Describes the query shapes a resource query provider can execute.
/// </summary>
/// <param name="ProviderKey">Stable provider key used to match capabilities to the active query provider.</param>
/// <param name="ProviderName">Human-readable provider name.</param>
/// <param name="SupportedScopes">Resource version scopes supported by the provider.</param>
/// <param name="RequiresActivationChannelForActiveScope">Whether active scope requires an activation channel.</param>
/// <param name="SupportedFilterTypes">Filter expression categories supported by the provider.</param>
/// <param name="SupportedLogicalOperators">Logical operators supported by the provider.</param>
/// <param name="SupportedComparisonOperators">Comparison operators supported for each filter category.</param>
/// <param name="SupportedMetadataFields">Metadata fields supported for filtering and sorting.</param>
/// <param name="MetadataContainsFields">Metadata fields that support containment filtering.</param>
/// <param name="SupportsMetadataSorting">Whether metadata sort expressions are supported.</param>
/// <param name="SupportsFacetSorting">Whether facet sort expressions are supported.</param>
/// <param name="SupportsSkip">Whether skip paging is supported.</param>
/// <param name="SupportsTake">Whether take paging is supported.</param>
/// <param name="FacetRangeSupport">Facet range value shapes supported by the provider.</param>
/// <param name="UnsupportedFeatures">Known unsupported features, for discovery and documentation.</param>
public sealed record QueryCapabilityDescription(
    string ProviderKey,
    string ProviderName,
    IReadOnlySet<ResourceVersionScope> SupportedScopes,
    bool RequiresActivationChannelForActiveScope,
    IReadOnlySet<QueryFilterType> SupportedFilterTypes,
    IReadOnlySet<LogicalOperator> SupportedLogicalOperators,
    IReadOnlyDictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>> SupportedComparisonOperators,
    IReadOnlySet<string> SupportedMetadataFields,
    IReadOnlySet<string> MetadataContainsFields,
    bool SupportsMetadataSorting,
    bool SupportsFacetSorting,
    bool SupportsSkip,
    bool SupportsTake,
    IReadOnlySet<QueryValueShape> FacetRangeSupport,
    IReadOnlyList<string> UnsupportedFeatures)
{
    /// <summary>
    /// Returns whether the provider supports the supplied comparison operator for the filter type.
    /// </summary>
    /// <param name="filterType">The filter category.</param>
    /// <param name="comparisonOperator">The comparison operator.</param>
    /// <returns><see langword="true"/> when the operator is supported for the filter category.</returns>
    public bool SupportsComparison(QueryFilterType filterType, ComparisonOperator comparisonOperator) =>
        SupportedComparisonOperators.TryGetValue(filterType, out var operators)
        && operators.Contains(comparisonOperator);
}

/// <summary>
/// Query filter expression categories used by provider capability descriptions.
/// </summary>
public enum QueryFilterType
{
    /// <summary>Metadata field filter.</summary>
    Metadata,

    /// <summary>Aspect presence filter.</summary>
    AspectPresence,

    /// <summary>Facet value filter.</summary>
    FacetValue,

    /// <summary>Logical filter expression.</summary>
    Logical,
}

/// <summary>
/// Value shapes relevant to query provider capability checks.
/// </summary>
public enum QueryValueShape
{
    /// <summary>String or string-like scalar values.</summary>
    String,

    /// <summary>Numeric scalar values.</summary>
    Numeric,

    /// <summary>Date or date-like scalar values.</summary>
    DateTime,
}
