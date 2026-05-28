using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleRestoreServiceTests : IDisposable
{
    private readonly ServiceProvider provider = LifecycleRestoreTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task RestoreAsync_ClearsArchivedAndSoftDeletedMarkers()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "soft-deleted");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "soft-deleted", ResourceLifecycleMarkerState.SoftDeleted);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("soft-deleted", ResourceLifecycleMarkerState.SoftDeleted),
            ],
        });

        Assert.Equal(2, result.RestoredCount);
        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "archived"));
        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "soft-deleted"));
    }

    [Fact]
    public async Task RestoreAsync_ClearsOnlySelectedMarkers()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "selected");
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "unselected");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "selected", ResourceLifecycleMarkerState.Archived);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "unselected", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("selected", ResourceLifecycleMarkerState.Archived)],
        });

        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "selected"));
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "unselected"));
    }
}
