using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantLifecycleMarkerTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task LifecycleMarkers_AreTenantScoped()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", tenantScope: TenantScopeTestFixtures.TenantA);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", tenantScope: TenantScopeTestFixtures.TenantB);
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ResourceId = "shared",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
        var query = provider.GetRequiredService<IResourceQueryService>();

        var tenantA = (await query.QueryAsync(new ResourceQuery
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();
        var tenantB = (await query.QueryAsync(new ResourceQuery
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        Assert.Single(tenantA);
        Assert.Empty(tenantB);
    }
}
