using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantResourceVersionHistoryTests : IDisposable
{
    private readonly ServiceProvider provider = ResourceVersionHistoryTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyRequestedTenantState()
    {
        await RegisterTenantResourceAsync(TenantScopeTestFixtures.TenantA, activeVersions: [1], ResourceLifecycleMarkerState.Archived);
        await RegisterTenantResourceAsync(TenantScopeTestFixtures.TenantB, activeVersions: [2], ResourceLifecycleMarkerState.SoftDeleted);

        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest
            {
                TenantScope = TenantScopeTestFixtures.TenantA,
                ResourceId = "shared",
            });

        Assert.Equal(TenantScopeTestFixtures.TenantA, result.TenantScope);
        Assert.Equal([1, 2], result.Versions.Select(static version => version.Version));
        Assert.Equal(["Published"], result.Versions[0].ActiveChannels);
        Assert.Empty(result.Versions[1].ActiveChannels);
        Assert.All(result.Versions, version => Assert.Equal(ResourceLifecycleMarkerState.Archived, version.LifecycleState));
    }

    [Fact]
    public async Task GetHistoryAsync_OmittedTenantUsesDefaultScope()
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionAsync(provider, "default", version: 1);

        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "default" });

        Assert.Equal(TenantScope.Default, result.TenantScope);
        Assert.Single(result.Versions);
    }

    private async Task RegisterTenantResourceAsync(
        TenantScope tenant,
        IReadOnlyList<int> activeVersions,
        ResourceLifecycleMarkerState lifecycleState)
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionAsync(provider, "shared", version: 1, tenant);
        await ResourceVersionHistoryTestFixtures.SaveVersionAsync(provider, "shared", version: 2, tenant);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "shared", "Published", activeVersions, tenant);
        await ResourceVersionHistoryTestFixtures.MarkAsync(provider, "shared", lifecycleState, tenant);
    }
}
