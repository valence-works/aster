using Aster.Core.Abstractions;
using Aster.Core.Models.Querying;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// Query capability declaration for the SQLite JSON query provider.
/// </summary>
public sealed class SqliteJsonQueryCapabilitiesProvider : IResourceQueryCapabilitiesProvider
{
    /// <summary>
    /// Stable provider key used by the SQLite JSON query provider and its capability declaration.
    /// </summary>
    public const string ProviderKey = "sqlite-json";

    /// <inheritdoc />
    public QueryCapabilityDescription Capabilities { get; } = new(
        ProviderKey: ProviderKey,
        ProviderName: "SQLite JSON",
        SupportedScopes: new HashSet<ResourceVersionScope>
        {
            ResourceVersionScope.Latest,
            ResourceVersionScope.AllVersions,
            ResourceVersionScope.Active,
            ResourceVersionScope.Draft,
        },
        RequiresActivationChannelForActiveScope: true,
        SupportedFilterTypes: new HashSet<QueryFilterType>
        {
            QueryFilterType.Metadata,
            QueryFilterType.AspectPresence,
            QueryFilterType.FacetValue,
            QueryFilterType.Logical,
        },
        SupportedLogicalOperators: new HashSet<LogicalOperator>
        {
            LogicalOperator.And,
            LogicalOperator.Or,
            LogicalOperator.Not,
        },
        SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>
        {
            [QueryFilterType.Metadata] = new HashSet<ComparisonOperator>
            {
                ComparisonOperator.Equals,
                ComparisonOperator.NotEquals,
                ComparisonOperator.In,
                ComparisonOperator.Contains,
                ComparisonOperator.StartsWith,
            },
            [QueryFilterType.FacetValue] = new HashSet<ComparisonOperator>
            {
                ComparisonOperator.Equals,
                ComparisonOperator.NotEquals,
                ComparisonOperator.In,
                ComparisonOperator.Contains,
                ComparisonOperator.StartsWith,
                ComparisonOperator.Range,
                ComparisonOperator.Exists,
            },
        },
        SupportedMetadataFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ResourceId",
            "Id",
            "DefinitionId",
            "Owner",
            "Version",
            "Created",
        },
        MetadataContainsFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ResourceId",
            "Id",
            "DefinitionId",
            "Owner",
            "Created",
        },
        SupportsMetadataSorting: true,
        SupportsFacetSorting: true,
        SupportsSkip: true,
        SupportsTake: true,
        FacetRangeSupport: new HashSet<QueryValueShape> { QueryValueShape.Numeric },
        UnsupportedFeatures:
        [
            "Metadata range filters",
            "Date-like facet ranges",
        ]);
}
