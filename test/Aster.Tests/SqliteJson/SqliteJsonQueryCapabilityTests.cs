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
        Assert.False(capabilities.SupportsFacetSorting);
        Assert.True(capabilities.SupportsSkip);
        Assert.True(capabilities.SupportsTake);

        Assert.Contains(ResourceVersionScope.Latest, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.AllVersions, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.Active, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.Draft, capabilities.SupportedScopes);
    }

    [Fact]
    public void Capabilities_ExcludeFacetSortingAndDateLikeFacetRanges()
    {
        Assert.False(capabilities.SupportsFacetSorting);
        Assert.Contains("Facet sorting", capabilities.UnsupportedFeatures);
        Assert.Contains(QueryValueShape.Numeric, capabilities.FacetRangeSupport);
        Assert.DoesNotContain(QueryValueShape.DateTime, capabilities.FacetRangeSupport);
        Assert.True(capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Range));
    }

    [Fact]
    public void Validator_RejectsFacetSortingForSqliteCapabilities()
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

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-facet-sort");
    }
}
