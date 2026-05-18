using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonQueryCapabilityTests
{
    private readonly QueryCapabilityDescription capabilities = new SqliteJsonQueryCapabilitiesProvider().Capabilities;

    [Fact]
    public void Capabilities_DeclareCurrentSqliteQuerySurface()
    {
        Assert.Equal(SqliteJsonQueryCapabilitiesProvider.ProviderKey, capabilities.ProviderKey);
        Assert.Equal("SQLite JSON", capabilities.ProviderName);
        Assert.True(capabilities.RequiresActivationChannelForActiveScope);
        Assert.True(capabilities.SupportsMetadataSorting);
        Assert.True(capabilities.SupportsFacetSorting);
        Assert.True(capabilities.SupportsSkip);
        Assert.True(capabilities.SupportsTake);

        Assert.Contains(ResourceVersionScope.Latest, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.AllVersions, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.Active, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.Draft, capabilities.SupportedScopes);

        AssertPortableOperators(QueryFilterType.Metadata);
        AssertPortableOperators(QueryFilterType.FacetValue);
        Assert.True(capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Exists));
    }

    [Fact]
    public void Capabilities_IncludeFacetSortingAndDateLikeFacetRanges()
    {
        Assert.True(capabilities.SupportsFacetSorting);
        Assert.DoesNotContain("Facet sorting", capabilities.UnsupportedFeatures);
        Assert.Contains(QueryValueShape.Numeric, capabilities.FacetRangeSupport);
        Assert.Contains(QueryValueShape.DateTime, capabilities.FacetRangeSupport);
        Assert.DoesNotContain("Date-like facet ranges", capabilities.UnsupportedFeatures);
        Assert.True(capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Range));
    }

    [Fact]
    public void Validator_AcceptsFacetSortingForSqliteCapabilities()
    {
        var validator = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
                options.InitializeSchema = false;
            })
            .BuildServiceProvider()
            .GetRequiredService<IResourceQueryValidator>();

        var result = validator.Validate(new ResourceQuery
        {
            Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
        });

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
    }

    private void AssertPortableOperators(QueryFilterType filterType)
    {
        Assert.True(capabilities.SupportsComparison(filterType, ComparisonOperator.NotEquals));
        Assert.True(capabilities.SupportsComparison(filterType, ComparisonOperator.In));
        Assert.True(capabilities.SupportsComparison(filterType, ComparisonOperator.StartsWith));
    }
}
