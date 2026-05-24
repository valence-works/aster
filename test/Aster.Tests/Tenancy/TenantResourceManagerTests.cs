using Aster.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantResourceManagerTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceManager manager;

    public TenantResourceManagerTests()
    {
        manager = provider.GetRequiredService<IResourceManager>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task Resources_WithSameId_AreIsolatedByTenant()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);

        await manager.UpdateAsync("shared-product", new UpdateResourceRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            BaseVersion = 1,
            AspectUpdates = { ["Title"] = new TenantScopeTestFixtures.TitleAspect("Tenant A v2") },
        });

        var tenantALatest = await manager.GetLatestVersionAsync("shared-product", TenantScopeTestFixtures.TenantA, CancellationToken.None);
        var tenantBLatest = await manager.GetLatestVersionAsync("shared-product", TenantScopeTestFixtures.TenantB, CancellationToken.None);

        Assert.Equal(2, tenantALatest!.Version);
        Assert.Equal(1, tenantBLatest!.Version);
    }
}
