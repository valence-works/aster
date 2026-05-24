using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SchemaVersions;

public sealed class TenantSchemaVersionServiceTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task UpgradeAsync_ResolvesDefinitionLineageWithinTenant()
    {
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var schemaVersions = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterDefinitionAsync(definitions, TenantScopeTestFixtures.TenantA);
        var tenantAResource = await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await RegisterDefinitionAsync(definitions, TenantScopeTestFixtures.TenantA);
        await RegisterDefinitionAsync(definitions, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);

        var result = await schemaVersions.UpgradeAsync("shared-product", new ResourceSchemaUpgradeRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            BaseVersion = tenantAResource.Version,
        });

        var tenantBLatest = await manager.GetLatestVersionAsync("shared-product", TenantScopeTestFixtures.TenantB);
        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Equal(2, result.Resource!.DefinitionVersion);
        Assert.Equal(TenantScopeTestFixtures.TenantA, result.Resource.TenantScope);
        Assert.Equal(1, tenantBLatest!.DefinitionVersion);
    }

    private static async Task RegisterDefinitionAsync(IResourceDefinitionStore store, Aster.Core.Models.Tenancy.TenantScope tenantScope)
    {
        await store.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build(), tenantScope);
    }
}
