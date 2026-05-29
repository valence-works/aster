using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleRestoreResultTests : IDisposable
{
    private readonly ServiceProvider provider = LifecycleRestoreTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task RestoreAsync_ReturnsAggregateCountsEmptyResultsAndOneResultPerInput()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "unmarked");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("unmarked", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("missing", ResourceLifecycleMarkerState.Archived),
            ],
        });
        var empty = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest());

        Assert.Equal(3, result.Candidates.Count);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(1, result.AlreadyRestoredCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Empty(empty.Candidates);
    }

    [Fact]
    public async Task RestoreAsync_DuplicateCandidatesAreDeterministicAndClearAtMostOnce()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
            ],
        });
        var retry = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived)],
        });

        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "archived"));
        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.AlreadyRestored, Assert.Single(retry.Candidates).Status);
    }
}
