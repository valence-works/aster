using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Lifecycle;
using Aster.Tests.Policies;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonLifecycleRestoreTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-restore-{Guid.NewGuid():N}.db");

    public void Dispose() => PolicyTestFixtures.DeleteSqliteFiles(databasePath);

    [Fact]
    public async Task RestoreAsync_ClearsPersistedMarkerAndUpdatesLifecycleQueries()
    {
        await using (var provider = LifecycleRestoreTestFixtures.CreateSqliteProvider(databasePath))
        {
            await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
            await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
            await provider.GetRequiredService<IResourceLifecycleRestoreService>().RestoreAsync(new ResourceLifecycleRestoreRequest
            {
                Candidates = [LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived)],
            });
        }

        await using var secondProvider = LifecycleRestoreTestFixtures.CreateSqliteProvider(databasePath);
        var query = secondProvider.GetRequiredService<IResourceQueryService>();

        var archived = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        Assert.Empty(archived);
    }

    [Fact]
    public async Task RestoreAsync_SqliteMarkerDeletesAreTenantScoped()
    {
        await using var provider = LifecycleRestoreTestFixtures.CreateSqliteProvider(databasePath);
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "shared", TenantScopeTestFixtures.TenantA);
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "shared", TenantScopeTestFixtures.TenantB);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "shared", ResourceLifecycleMarkerState.SoftDeleted, TenantScopeTestFixtures.TenantA);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "shared", ResourceLifecycleMarkerState.SoftDeleted, TenantScopeTestFixtures.TenantB);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            Candidates = [LifecycleRestoreTestFixtures.Candidate("shared", ResourceLifecycleMarkerState.SoftDeleted)],
        });

        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "shared", TenantScopeTestFixtures.TenantA));
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "shared", TenantScopeTestFixtures.TenantB));
    }

    [Fact]
    public async Task ClearMarkerAsync_OnlyDeletesMatchingExpectedState()
    {
        await using var provider = LifecycleRestoreTestFixtures.CreateSqliteProvider(databasePath);
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        var clearStore = provider.GetRequiredService<IResourceLifecycleMarkerClearStore>();

        var wrongStateRemoved = await clearStore.ClearMarkerAsync(
            "archived",
            TenantScope.Default,
            ResourceLifecycleMarkerState.SoftDeleted);
        var rightStateRemoved = await clearStore.ClearMarkerAsync(
            "archived",
            TenantScope.Default,
            ResourceLifecycleMarkerState.Archived);

        Assert.False(wrongStateRemoved);
        Assert.True(rightStateRemoved);
    }
}
