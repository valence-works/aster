using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonLifecycleMarkerTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-marker-{Guid.NewGuid():N}.db");

    public void Dispose() => PolicyTestFixtures.DeleteSqliteFiles(databasePath);

    [Fact]
    public async Task LifecycleMarkers_PersistAndCanBeQueried()
    {
        await using (var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath))
        {
            await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
            await PolicyTestFixtures.SaveResourceAsync(provider, "archived");
            await PolicyTestFixtures.SaveResourceAsync(provider, "unmarked");
            await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                ResourceId = "archived",
                State = ResourceLifecycleMarkerState.Archived,
                MarkedAt = DateTimeOffset.UtcNow,
            });
        }

        await using var secondProvider = PolicyTestFixtures.CreateSqliteProvider(databasePath);
        var query = secondProvider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        var result = Assert.Single(results);
        Assert.Equal("archived", result.ResourceId);
    }

    [Fact]
    public async Task QueryAsync_LifecycleStateFilterRunsBeforePaging()
    {
        await using var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "a-unmarked");
        await PolicyTestFixtures.SaveResourceAsync(provider, "b-archived");
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "b-archived",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
            Take = 1,
        })).ToList();

        var result = Assert.Single(results);
        Assert.Equal("b-archived", result.ResourceId);
    }
}
