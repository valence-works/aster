using Aster.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantDefaultScopeCompatibilityTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceManager manager;

    public TenantDefaultScopeCompatibilityTests()
    {
        manager = provider.GetRequiredService<IResourceManager>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task OmittedTenantScope_ContinuesToUseDefaultTenant()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Default");
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);

        await manager.UpdateAsync("shared-product", new UpdateResourceRequest { BaseVersion = 1 });

        var defaultLatest = await manager.GetLatestVersionAsync("shared-product");
        var tenantALatest = await manager.GetLatestVersionAsync("shared-product", TenantScopeTestFixtures.TenantA, CancellationToken.None);

        Assert.Equal(2, defaultLatest!.Version);
        Assert.Equal(1, tenantALatest!.Version);
    }
}
