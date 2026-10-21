using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class TenantPortabilityStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task ExportAsync_UsesTenantScopedSqliteStoreReads()
    {
        await using var provider = Database.CreateProvider();
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
}

public sealed class SqliteJsonTenantPortabilityStoreTests() : TenantPortabilityStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbTenantPortabilityStoreTests() : TenantPortabilityStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonTenantPortabilityStoreTests() : TenantPortabilityStoreTests(new SqlServerTestDatabase());
