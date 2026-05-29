using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
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

        await manager.ActivateAsync("shared-product", 2, "Published", TenantScopeTestFixtures.TenantA, allowMultipleActive: false, CancellationToken.None);
        await manager.ActivateAsync("shared-product", 1, "Published", TenantScopeTestFixtures.TenantB, allowMultipleActive: false, CancellationToken.None);

        var tenantAActive = (await manager.GetActiveVersionsAsync("shared-product", "Published", TenantScopeTestFixtures.TenantA, CancellationToken.None)).ToList();
        var tenantBActive = (await manager.GetActiveVersionsAsync("shared-product", "Published", TenantScopeTestFixtures.TenantB, CancellationToken.None)).ToList();

        Assert.Equal(2, tenantAActive.Single().Version);
        Assert.Equal(1, tenantBActive.Single().Version);
    }

    [Fact]
    public async Task HistoricalActivation_IsScopedByTenant()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);
        await manager.UpdateAsync("shared-product", new UpdateResourceRequest { TenantScope = TenantScopeTestFixtures.TenantA, BaseVersion = 1 });
        await manager.UpdateAsync("shared-product", new UpdateResourceRequest { TenantScope = TenantScopeTestFixtures.TenantB, BaseVersion = 1 });
        await manager.ActivateAsync("shared-product", 2, "Published", TenantScopeTestFixtures.TenantB, allowMultipleActive: false, CancellationToken.None);

        await manager.ActivateAsync("shared-product", 1, "Published", TenantScopeTestFixtures.TenantA, allowMultipleActive: false, CancellationToken.None);

        var tenantAActive = (await manager.GetActiveVersionsAsync("shared-product", "Published", TenantScopeTestFixtures.TenantA, CancellationToken.None)).ToList();
        var tenantBActive = (await manager.GetActiveVersionsAsync("shared-product", "Published", TenantScopeTestFixtures.TenantB, CancellationToken.None)).ToList();
        var tenantALatest = await manager.GetLatestVersionAsync("shared-product", TenantScopeTestFixtures.TenantA, CancellationToken.None);
        var tenantBLatest = await manager.GetLatestVersionAsync("shared-product", TenantScopeTestFixtures.TenantB, CancellationToken.None);

        Assert.Equal(1, tenantAActive.Single().Version);
        Assert.Equal(2, tenantBActive.Single().Version);
        Assert.Equal(2, tenantALatest!.Version);
        Assert.Equal(2, tenantBLatest!.Version);
    }

    [Fact]
    public async Task UpdateActivationAsync_NormalizesPayloadIdentityToMethodArguments()
    {
        var writer = provider.GetRequiredService<IResourceVersionWriter>();

        var state = await writer.UpdateActivationAsync("product-1", "Published", new ActivationState
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ResourceId = "wrong-product",
            Channel = "Wrong",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });

        Assert.Equal(TenantScopeTestFixtures.TenantA, state.TenantScope);
        Assert.Equal("product-1", state.ResourceId);
        Assert.Equal("Published", state.Channel);
    }
}
