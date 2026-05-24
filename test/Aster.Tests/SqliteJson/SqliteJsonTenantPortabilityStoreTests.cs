using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonTenantPortabilityStoreTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-tenant-portability-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    [Fact]
    public async Task ExportAsync_UsesTenantScopedSqliteStoreReads()
    {
        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant A", TenantScopeTestFixtures.TenantA);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "shared-product", "Tenant B", TenantScopeTestFixtures.TenantB);

        var portability = provider.GetRequiredService<IResourcePortabilityService>();
        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["shared-product"],
        });

        Assert.NotNull(result.Snapshot);
        var resource = Assert.Single(result.Snapshot.Resources);
        Assert.Equal(TenantScopeTestFixtures.TenantB, resource.TenantScope);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
