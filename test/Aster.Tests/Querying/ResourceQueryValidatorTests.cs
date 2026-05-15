using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;

namespace Aster.Tests.Querying;

public sealed class ResourceQueryValidatorTests
{
    private readonly ResourceQueryValidator inMemoryValidator = new([new InMemoryQueryCapabilitiesProvider()]);
    private readonly ResourceQueryValidator sqliteValidator = new([new SqliteJsonQueryCapabilitiesProvider()]);

    [Fact]
    public void Validate_WithSupportedQuery_ReturnsSuccessWithoutMutatingQuery()
    {
        var query = new ResourceQuery
        {
            Scope = ResourceVersionScope.Latest,
            Filter = new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
            Sorts = [new SortExpression("Created", SortDirection.Descending)],
            Skip = 1,
            Take = 10,
        };

        var before = query;
        var result = sqliteValidator.Validate(query);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
        Assert.Equal(before, query);
    }

    [Fact]
    public void Validate_WithMultipleUnsupportedFeatures_ReturnsAllDetectableFailures()
    {
        var result = sqliteValidator.Validate(new ResourceQuery
        {
            Scope = ResourceVersionScope.Active,
            Skip = -1,
            Take = -1,
            Filter = new LogicalExpression(LogicalOperator.And, [
                new MetadataFilter("Unknown", "value", ComparisonOperator.Range),
                new FacetValueFilter(
                    "Price",
                    "Amount",
                    new RangeValue(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
                    ComparisonOperator.Range),
            ]),
            Sorts = [new SortExpression("Title", AspectKey: "Title")],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "activation-channel-required");
        Assert.Contains(result.Failures, failure => failure.Code == "negative-skip");
        Assert.Contains(result.Failures, failure => failure.Code == "negative-take");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-metadata-field");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-comparison-operator");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-range-value-shape");
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-facet-sort");
    }

    [Fact]
    public void Validate_WithoutCapabilities_FailsClosed()
    {
        var result = new ResourceQueryValidator([]).Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains("not declared", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ManualResourceQueriesRemainSupported()
    {
        var query = new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new LogicalExpression(LogicalOperator.And, [
                new AspectPresenceFilter("Title"),
                new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
            ]),
        };

        var result = inMemoryValidator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InMemoryAndSqliteReflectProviderDifferences()
    {
        var dateRangeQuery = new ResourceQuery
        {
            Filter = new FacetValueFilter(
                "Schedule",
                "StartsAt",
                new RangeValue(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
                ComparisonOperator.Range),
            Sorts = [new SortExpression("StartsAt", AspectKey: "Schedule")],
        };

        Assert.True(inMemoryValidator.Validate(dateRangeQuery).IsValid);

        var sqliteResult = sqliteValidator.Validate(dateRangeQuery);

        Assert.False(sqliteResult.IsValid);
        Assert.Contains(sqliteResult.Failures, failure => failure.Code == "unsupported-range-value-shape");
        Assert.Contains(sqliteResult.Failures, failure => failure.Code == "unsupported-facet-sort");
    }

    private sealed class EmptyCapabilitiesProvider : IResourceQueryCapabilitiesProvider
    {
        public QueryCapabilityDescription Capabilities { get; } = new(
            ProviderName: "Empty",
            SupportedScopes: new HashSet<ResourceVersionScope>(),
            RequiresActivationChannelForActiveScope: false,
            SupportedFilterTypes: new HashSet<QueryFilterType>(),
            SupportedLogicalOperators: new HashSet<LogicalOperator>(),
            SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>(),
            SupportedMetadataFields: new HashSet<string>(),
            SupportsMetadataSorting: false,
            SupportsFacetSorting: false,
            SupportsSkip: false,
            SupportsTake: false,
            FacetRangeSupport: new HashSet<QueryValueShape>(),
            UnsupportedFeatures: []);
    }
}
