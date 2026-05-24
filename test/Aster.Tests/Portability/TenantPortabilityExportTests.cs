using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class TenantPortabilityExportTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ExportAsync_ExportsOnlyRequestedTenant()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);

        var portability = provider.GetRequiredService<IResourcePortabilityService>();
        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["shared-product"],
        });

        Assert.NotNull(result.Snapshot);
        Assert.Equal(TenantScopeTestFixtures.TenantA, result.SourceTenantScope);
        Assert.Equal(TenantScopeTestFixtures.TenantA, result.Snapshot.SourceTenantScope);
        Assert.All(result.Snapshot.Resources, resource => Assert.Equal(TenantScopeTestFixtures.TenantA, resource.TenantScope));
    }
}
