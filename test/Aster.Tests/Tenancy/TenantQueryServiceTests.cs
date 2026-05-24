using Aster.Core.Abstractions;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantQueryServiceTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceQueryService query;

    public TenantQueryServiceTests()
    {
        query = provider.GetRequiredService<IResourceQueryService>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task QueryAsync_ReturnsOnlyRequestedTenant()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product-a", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product-b", "Tenant B", TenantScopeTestFixtures.TenantB);

        var results = (await query.QueryAsync(TenantScopeTestFixtures.ProductQuery(TenantScopeTestFixtures.TenantA))).ToList();

        var result = Assert.Single(results);
        Assert.Equal("product-a", result.ResourceId);
        Assert.Equal(TenantScopeTestFixtures.TenantA, result.TenantScope);
    }
}
