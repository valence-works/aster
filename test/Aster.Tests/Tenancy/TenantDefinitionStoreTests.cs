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
        await store.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA, CancellationToken.None);
        await store.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA, CancellationToken.None);
        await store.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantB, CancellationToken.None);

        var tenantALatest = await store.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None);
        var tenantBLatest = await store.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantB, CancellationToken.None);
        var tenantBDefinitions = (await store.ListDefinitionsAsync(TenantScopeTestFixtures.TenantB, CancellationToken.None)).ToList();

        Assert.Equal(2, tenantALatest!.Version);
        Assert.Equal(1, tenantBLatest!.Version);
        Assert.Single(tenantBDefinitions);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_WithoutTenantScope_AlwaysUsesDefaultTenant()
    {
        var definition = CreateDefinition() with { TenantScope = TenantScopeTestFixtures.TenantA };

        await store.RegisterDefinitionAsync(definition);

        Assert.NotNull(await store.GetDefinitionAsync("Product"));
        Assert.Null(await store.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None));
    }

    private static Aster.Core.Models.Definitions.ResourceDefinition CreateDefinition() =>
        new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();
}
