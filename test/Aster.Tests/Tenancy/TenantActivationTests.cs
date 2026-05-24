using Aster.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantActivationTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceManager manager;

    public TenantActivationTests()
    {
        manager = provider.GetRequiredService<IResourceManager>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ActivationState_IsScopedByTenant()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);
        await manager.UpdateAsync("shared-product", new UpdateResourceRequest { TenantScope = TenantScopeTestFixtures.TenantA, BaseVersion = 1 });

        await manager.ActivateAsync("shared-product", 2, "Published", TenantScopeTestFixtures.TenantA);
        await manager.ActivateAsync("shared-product", 1, "Published", TenantScopeTestFixtures.TenantB);

        var tenantAActive = (await manager.GetActiveVersionsAsync("shared-product", "Published", TenantScopeTestFixtures.TenantA)).ToList();
        var tenantBActive = (await manager.GetActiveVersionsAsync("shared-product", "Published", TenantScopeTestFixtures.TenantB)).ToList();

        Assert.Equal(2, tenantAActive.Single().Version);
        Assert.Equal(1, tenantBActive.Single().Version);
    }
}
