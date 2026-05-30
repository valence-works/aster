using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Aster.Tests.Lifecycle;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Operational;

public sealed class OperationalHardeningTests : IDisposable
{
    private readonly List<string> sqliteDatabasePaths = [];

    public void Dispose()
    {
        foreach (var databasePath in sqliteDatabasePaths)
            PolicyTestFixtures.DeleteSqliteFiles(databasePath);
    }

    [Fact]
    public async Task RestoreAsync_RetryReportsAlreadyRestoredAndLeavesMarkerCleared()
    {
        using var provider = LifecycleRestoreTestFixtures.CreateCoreProvider();
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "retry-restore");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "retry-restore", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var first = await restore.RestoreAsync(RestoreRequest("retry-restore", ResourceLifecycleMarkerState.Archived));
        var retry = await restore.RestoreAsync(RestoreRequest("retry-restore", ResourceLifecycleMarkerState.Archived));

        Assert.Equal(1, first.RestoredCount);
        Assert.Equal(1, retry.AlreadyRestoredCount);
        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "retry-restore"));
    }

    [Fact]
    public async Task RestoreAsync_ConcurrentSameCandidateLeavesMarkerCleared()
    {
        using var provider = LifecycleRestoreTestFixtures.CreateCoreProvider();
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "concurrent-restore");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "concurrent-restore", ResourceLifecycleMarkerState.Archived);
        var markerStore = new CoordinatedMarkerClearStore(provider.GetRequiredService<IResourceLifecycleMarkerClearStore>());
        var restore = new ResourceLifecycleRestoreService(
            provider.GetRequiredService<IResourceVersionReader>(),
            markerStore);

        var results = await Task.WhenAll(
            restore.RestoreAsync(RestoreRequest("concurrent-restore", ResourceLifecycleMarkerState.Archived)).AsTask(),
            restore.RestoreAsync(RestoreRequest("concurrent-restore", ResourceLifecycleMarkerState.Archived)).AsTask());

        Assert.Equal(1, results.Sum(static result => result.RestoredCount));
        Assert.Equal(1, results.Sum(static result => result.AlreadyRestoredCount));
        Assert.Equal(2, markerStore.ClearAttempts);
        Assert.Null(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "concurrent-restore"));
    }

    [Fact]
    public async Task PruningApplication_RetryReportsAlreadyPrunedAndDoesNotRemoveAdditionalVersions()
    {
        using var provider = PolicyTestFixtures.CreateCoreProvider();
        await RegisterPrunableResourceAsync(provider, "retry-prune");
        var pruning = provider.GetRequiredService<IResourcePolicyPruningApplicationService>();

        var first = await pruning.ApplyAsync(PruningRequest("retry-prune", resourceVersion: 1));
        var retry = await pruning.ApplyAsync(PruningRequest("retry-prune", resourceVersion: 1));

        Assert.Equal(1, first.PrunedCount);
        Assert.Equal(1, retry.AlreadyPrunedCount);
        Assert.Equal([2, 3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "retry-prune")).Select(static version => version.Version).ToList());
    }

    [Fact]
    public async Task PruningApplication_SqliteRetryAfterReopenReportsAlreadyPruned()
    {
        var databasePath = NewSqliteDatabasePath("aster-hardening-prune");
        await using (var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath))
        {
            await RegisterPrunableResourceAsync(provider, "persisted-prune");

            var first = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
                PruningRequest("persisted-prune", resourceVersion: 1));

            Assert.Equal(1, first.PrunedCount);
        }

        await using var secondProvider = PolicyTestFixtures.CreateSqliteProvider(databasePath);

        var retry = await secondProvider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            PruningRequest("persisted-prune", resourceVersion: 1));

        Assert.Equal(1, retry.AlreadyPrunedCount);
        Assert.Equal([2, 3], (await PolicyTestFixtures.ReadVersionsAsync(secondProvider, "persisted-prune")).Select(static version => version.Version).ToList());
    }

    [Fact]
    public async Task HistoricalActivation_SingleActiveRetryLeavesOneActiveVersionAndLatestUnchanged()
    {
        using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        var manager = provider.GetRequiredService<IResourceManager>();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = v1.Version });

        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Published");
        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Published");

        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();
        var latest = await manager.GetLatestVersionAsync(v1.ResourceId);

        Assert.Equal([v1.Version], active.Select(static version => version.Version).ToList());
        Assert.NotNull(latest);
        Assert.Equal(v2.Version, latest.Version);
    }

    [Fact]
    public async Task HistoricalActivation_MultiActiveRetryLeavesUniqueOrderedActiveVersionsAndLatestUnchanged()
    {
        using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        var manager = provider.GetRequiredService<IResourceManager>();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = v1.Version });
        var v3 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = v2.Version });

        await manager.ActivateAsync(v1.ResourceId, v2.Version, "Preview", allowMultipleActive: true);
        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Preview", allowMultipleActive: true);
        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Preview", allowMultipleActive: true);

        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Preview")).ToList();
        var latest = await manager.GetLatestVersionAsync(v1.ResourceId);

        Assert.Equal([v1.Version, v2.Version], active.Select(static version => version.Version).ToList());
        Assert.NotNull(latest);
        Assert.Equal(v3.Version, latest.Version);
    }

    private static ResourceLifecycleRestoreRequest RestoreRequest(
        string resourceId,
        ResourceLifecycleMarkerState expectedState) =>
        new()
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate(resourceId, expectedState)],
        };

    private static ResourcePolicyPruningApplicationRequest PruningRequest(
        string resourceId,
        int resourceVersion) =>
        new()
        {
            Candidates = [PolicyTestFixtures.PruningCandidate(resourceId, resourceVersion)],
        };

    private static async Task RegisterPrunableResourceAsync(
        IServiceProvider provider,
        string resourceId)
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, version: 3);
    }

    private string NewSqliteDatabasePath(string prefix)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
        sqliteDatabasePaths.Add(databasePath);
        return databasePath;
    }

    private sealed class CoordinatedMarkerClearStore : IResourceLifecycleMarkerClearStore
    {
        private readonly IResourceLifecycleMarkerClearStore inner;
        private readonly TaskCompletionSource bothClearAttemptsArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int clearAttempts;

        public CoordinatedMarkerClearStore(IResourceLifecycleMarkerClearStore inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            this.inner = inner;
        }

        public int ClearAttempts => Volatile.Read(ref clearAttempts);

        public ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(
            string resourceId,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            inner.GetMarkerAsync(resourceId, tenantScope, cancellationToken);

        public ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
            IEnumerable<string> resourceIds,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            inner.GetMarkersAsync(resourceIds, tenantScope, cancellationToken);

        public ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
            ResourceLifecycleMarker marker,
            CancellationToken cancellationToken = default) =>
            inner.SaveMarkerAsync(marker, cancellationToken);

        public async ValueTask<bool> ClearMarkerAsync(
            string resourceId,
            TenantScope tenantScope,
            ResourceLifecycleMarkerState expectedState,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref clearAttempts) == 2)
                bothClearAttemptsArrived.TrySetResult();

            await bothClearAttemptsArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return await inner.ClearMarkerAsync(resourceId, tenantScope, expectedState, cancellationToken);
        }
    }
}
