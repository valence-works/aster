using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Tests.Policies;
using Aster.Tests.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonResourceVersionHistoryTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-history-{Guid.NewGuid():N}.db");

    public void Dispose() => PolicyTestFixtures.DeleteSqliteFiles(databasePath);

    [Fact]
    public async Task GetHistoryAsync_ReturnsPersistedVersionSummarySemantics()
    {
        await using (var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath))
        {
            await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "persisted", versionCount: 5);
            await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "persisted", "Published", [2]);
            await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "persisted", "Preview", [2, 4]);
            await ResourceVersionHistoryTestFixtures.MarkAsync(provider, "persisted", ResourceLifecycleMarkerState.SoftDeleted);
        }

        await using var secondProvider = PolicyTestFixtures.CreateSqliteProvider(databasePath);

        var result = await secondProvider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "persisted" });

        Assert.Equal([1, 2, 3, 4, 5], result.Versions.Select(static version => version.Version));
        Assert.Equal(ResourceVersionMaintenanceDisposition.PossibleCandidate, result.Versions[0].MaintenanceDisposition);
        Assert.Equal(["Preview", "Published"], result.Versions[1].ActiveChannels);
        Assert.Equal(["Preview"], result.Versions[3].ActiveChannels);
        Assert.True(result.Versions[4].IsLatest);
        Assert.True(result.Versions[4].IsProtectedFromPruning);
        Assert.All(result.Versions, version => Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, version.LifecycleState));
    }

    [Fact]
    public async Task GetHistoriesAsync_ReturnsPersistedBatchHistorySemantics()
    {
        await using (var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath))
        {
            await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "persisted-a", versionCount: 3);
            await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "persisted-b", versionCount: 2);
            await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "persisted-a", "Published", [2]);
            await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "persisted-b", "Preview", [1]);
            await ResourceVersionHistoryTestFixtures.MarkAsync(provider, "persisted-a", ResourceLifecycleMarkerState.Archived);
        }

        await using var secondProvider = PolicyTestFixtures.CreateSqliteProvider(databasePath);

        var result = await secondProvider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
            new ResourceVersionHistoryBatchRequest
            {
                ResourceIds = ["persisted-b", "persisted-a", "persisted-b", "missing"],
            });

        Assert.Equal(["persisted-b", "persisted-a", "missing"], result.Histories.Select(static history => history.ResourceId));
        Assert.Equal([1, 2], result.Histories[0].Versions.Select(static version => version.Version));
        Assert.Equal([1, 2, 3], result.Histories[1].Versions.Select(static version => version.Version));
        Assert.Empty(result.Histories[2].Versions);
        Assert.Equal(["Preview"], result.Histories[0].Versions[0].ActiveChannels);
        Assert.Equal(["Published"], result.Histories[1].Versions[1].ActiveChannels);
        Assert.All(result.Histories[1].Versions, version => Assert.Equal(ResourceLifecycleMarkerState.Archived, version.LifecycleState));
    }

}
