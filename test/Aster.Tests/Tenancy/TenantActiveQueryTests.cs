using Aster.Core.Abstractions;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantActiveQueryTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceManager manager;
    private readonly IResourceQueryService query;

    public TenantActiveQueryTests()
    {
        manager = provider.GetRequiredService<IResourceManager>();
        query = provider.GetRequiredService<IResourceQueryService>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ActiveQuery_UsesTenantScopedActivationState()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);
        await manager.UpdateAsync("shared-product", new UpdateResourceRequest { TenantScope = TenantScopeTestFixtures.TenantB, BaseVersion = 1 });
        await manager.ActivateAsync("shared-product", 1, "Published", TenantScopeTestFixtures.TenantA, allowMultipleActive: false, CancellationToken.None);
        await manager.ActivateAsync("shared-product", 2, "Published", TenantScopeTestFixtures.TenantB, allowMultipleActive: false, CancellationToken.None);

        var results = (await query.QueryAsync(new ResourceQuery
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
            DefinitionId = "Product",
        })).ToList();

        var result = Assert.Single(results);
        Assert.Equal(2, result.Version);
        Assert.Equal(TenantScopeTestFixtures.TenantB, result.TenantScope);
    }
}
