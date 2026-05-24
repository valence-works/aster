using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantDefinitionStoreTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceDefinitionStore store;

    public TenantDefinitionStoreTests()
    {
        store = provider.GetRequiredService<IResourceDefinitionStore>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task Definitions_AreVersionedIndependentlyPerTenant()
    {
        await store.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA);
        await store.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA);
        await store.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantB);

        var tenantALatest = await store.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA);
        var tenantBLatest = await store.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantB);
        var tenantBDefinitions = (await store.ListDefinitionsAsync(TenantScopeTestFixtures.TenantB)).ToList();

        Assert.Equal(2, tenantALatest!.Version);
        Assert.Equal(1, tenantBLatest!.Version);
        Assert.Single(tenantBDefinitions);
    }

    private static Aster.Core.Models.Definitions.ResourceDefinition CreateDefinition() =>
        new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();
}
