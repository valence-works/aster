using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Tests.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantLifecycleRestoreTests : IDisposable
{
    private readonly ServiceProvider provider = LifecycleRestoreTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task RestoreAsync_ClearsOnlyMarkersInsideEffectiveTenant()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "shared", TenantScopeTestFixtures.TenantA);
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "shared", TenantScopeTestFixtures.TenantB);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "shared", ResourceLifecycleMarkerState.Archived, TenantScopeTestFixtures.TenantA);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "shared", ResourceLifecycleMarkerState.Archived, TenantScopeTestFixtures.TenantB);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            Candidates = [LifecycleRestoreTestFixtures.Candidate("shared", ResourceLifecycleMarkerState.Archived)],
        });

        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "shared", TenantScopeTestFixtures.TenantA));
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "shared", TenantScopeTestFixtures.TenantB));
    }

    [Fact]
    public async Task RestoreAsync_OmittedTenantScopeUsesDefaultTenant()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "default-product");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "default-product", ResourceLifecycleMarkerState.SoftDeleted);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("default-product", ResourceLifecycleMarkerState.SoftDeleted)],
        });

        Assert.True(result.TenantScope.IsDefault);
        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "default-product"));
    }
}
