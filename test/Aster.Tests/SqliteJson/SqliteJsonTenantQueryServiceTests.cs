using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonTenantQueryServiceTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-tenant-query-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    [Fact]
    public async Task QueryAsync_FiltersLatestActiveAndDraftByTenant()
    {
        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var query = provider.GetRequiredService<IResourceQueryService>();

        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant A", TenantScopeTestFixtures.TenantA));
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant B v1", TenantScopeTestFixtures.TenantB));
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant B v2", TenantScopeTestFixtures.TenantB, version: 2));
        await writer.UpdateActivationAsync("shared-product", "Published", new ActivationState
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            ResourceId = "shared-product",
            Channel = "Published",
            ActiveVersions = [2],
            LastUpdated = DateTime.UtcNow,
        });

        var latest = (await query.QueryAsync(TenantScopeTestFixtures.ProductQuery(TenantScopeTestFixtures.TenantB))).ToList();
        var active = (await query.QueryAsync(new ResourceQuery
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            DefinitionId = "Product",
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        })).ToList();
        var draft = (await query.QueryAsync(new ResourceQuery
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            DefinitionId = "Product",
            Scope = ResourceVersionScope.Draft,
        })).ToList();

        Assert.Equal(2, latest.Single().Version);
        Assert.Equal(2, active.Single().Version);
        Assert.Equal(1, draft.Single().Version);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
