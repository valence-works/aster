using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Querying;

public sealed class QueryCapabilityDiscoveryTests
{
    [Fact]
    public void AddAsterCore_RegistersInMemoryCapabilities()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;

        Assert.Equal("In-memory", capabilities.ProviderName);
        Assert.True(capabilities.SupportsFacetSorting);
    }

    [Fact]
    public void AddAsterSqliteJson_RegistersSqliteCapabilitiesAsActiveProvider()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
                options.InitializeSchema = false;
            })
            .BuildServiceProvider();

        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;
        var validator = provider.GetRequiredService<IResourceQueryValidator>();

        Assert.Equal("SQLite JSON", capabilities.ProviderName);
        Assert.False(capabilities.SupportsFacetSorting);
        Assert.False(validator.Validate(new ResourceQuery
        {
            Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
        }).IsValid);
    }

    [Fact]
    public void Capabilities_ExposeProviderDifferences()
    {
        var inMemory = new Core.InMemory.InMemoryQueryCapabilitiesProvider().Capabilities;
        var sqlite = new SqliteJsonQueryCapabilitiesProvider().Capabilities;

        Assert.True(inMemory.SupportsFacetSorting);
        Assert.False(sqlite.SupportsFacetSorting);
        Assert.Contains(QueryValueShape.DateTime, inMemory.FacetRangeSupport);
        Assert.DoesNotContain(QueryValueShape.DateTime, sqlite.FacetRangeSupport);
    }
}
