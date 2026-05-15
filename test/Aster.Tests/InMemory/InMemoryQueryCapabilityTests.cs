using Aster.Core.InMemory;
using Aster.Core.Models.Querying;

namespace Aster.Tests.InMemory;

public sealed class InMemoryQueryCapabilityTests
{
    private readonly QueryCapabilityDescription capabilities = new InMemoryQueryCapabilitiesProvider().Capabilities;

    [Fact]
    public void Capabilities_DeclareCurrentInMemoryQuerySurface()
    {
        Assert.Equal(InMemoryQueryCapabilitiesProvider.ProviderKey, capabilities.ProviderKey);
        Assert.Equal("In-memory", capabilities.ProviderName);
        Assert.True(capabilities.RequiresActivationChannelForActiveScope);
        Assert.True(capabilities.SupportsMetadataSorting);
        Assert.True(capabilities.SupportsFacetSorting);
        Assert.True(capabilities.SupportsSkip);
        Assert.True(capabilities.SupportsTake);

        Assert.Contains(ResourceVersionScope.Latest, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.AllVersions, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.Active, capabilities.SupportedScopes);
        Assert.Contains(ResourceVersionScope.Draft, capabilities.SupportedScopes);

        Assert.Contains(QueryFilterType.Metadata, capabilities.SupportedFilterTypes);
        Assert.Contains(QueryFilterType.AspectPresence, capabilities.SupportedFilterTypes);
        Assert.Contains(QueryFilterType.FacetValue, capabilities.SupportedFilterTypes);
        Assert.Contains(QueryFilterType.Logical, capabilities.SupportedFilterTypes);
    }

    [Fact]
    public void Capabilities_IncludeFacetSortingAndDateLikeFacetRanges()
    {
        Assert.True(capabilities.SupportsFacetSorting);
        Assert.Contains(QueryValueShape.Numeric, capabilities.FacetRangeSupport);
        Assert.Contains(QueryValueShape.DateTime, capabilities.FacetRangeSupport);
        Assert.True(capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Range));
    }
}
