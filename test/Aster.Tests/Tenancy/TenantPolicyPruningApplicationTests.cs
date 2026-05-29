using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantPolicyPruningApplicationTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_PrunesOnlyRequestedTenant()
    {
        await RegisterTenantResourceAsync(TenantScopeTestFixtures.TenantA);
        await RegisterTenantResourceAsync(TenantScopeTestFixtures.TenantB);

        var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                TenantScope = TenantScopeTestFixtures.TenantA,
                Candidates = [PolicyTestFixtures.PruningCandidate("shared", resourceVersion: 1)],
            });

        Assert.Equal(1, result.PrunedCount);
        Assert.Equal([2, 3], await ReadVersionsAsync(TenantScopeTestFixtures.TenantA));
        Assert.Equal([1, 2, 3], await ReadVersionsAsync(TenantScopeTestFixtures.TenantB));
    }

    private async Task RegisterTenantResourceAsync(TenantScope tenant)
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, tenant, PolicyTestFixtures.PruningPolicy(retainedVersions: 2));
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", version: 1, tenantScope: tenant);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", version: 2, tenantScope: tenant);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", version: 3, tenantScope: tenant);
    }

    private async Task<List<int>> ReadVersionsAsync(TenantScope tenant) =>
        (await PolicyTestFixtures.ReadVersionsAsync(provider, "shared", tenant)).Select(static version => version.Version).ToList();
}
