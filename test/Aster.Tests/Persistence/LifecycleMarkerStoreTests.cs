using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class LifecycleMarkerStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task LifecycleMarkers_PersistAndCanBeQueried()
    {
        await using (var provider = Database.CreateProvider())
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

        await using var secondProvider = Database.CreateProvider();
        var query = secondProvider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        var result = Assert.Single(results);
        Assert.Equal("archived", result.ResourceId);
    }

    [SkippableFact]
    public async Task QueryAsync_LifecycleStateFilterRunsBeforePaging()
    {
        await using var provider = Database.CreateProvider();
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

public sealed class SqliteJsonLifecycleMarkerTests() : LifecycleMarkerStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbLifecycleMarkerTests() : LifecycleMarkerStoreTests(new PostgresTestDatabase());
