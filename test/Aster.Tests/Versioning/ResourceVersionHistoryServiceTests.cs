using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Versioning;

public sealed class ResourceVersionHistoryServiceTests : IDisposable
{
    private readonly ServiceProvider provider = ResourceVersionHistoryTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task GetHistoryAsync_ReturnsOrderedVersionSummariesWithLatestDraftAndActiveChannels()
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "history", versionCount: 5);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "history", "Published", [2]);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "history", "Preview", [2, 4]);

        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "history" });

        Assert.Equal("history", result.ResourceId);
        Assert.Equal([1, 2, 3, 4, 5], result.Versions.Select(static version => version.Version));

        AssertVersion(result.Versions[0], version: 1, isLatest: false, isDraft: true, [], ResourceVersionMaintenanceDisposition.PossibleCandidate);
        AssertVersion(result.Versions[1], version: 2, isLatest: false, isDraft: false, ["Preview", "Published"], ResourceVersionMaintenanceDisposition.Protected);
        AssertVersion(result.Versions[2], version: 3, isLatest: false, isDraft: true, [], ResourceVersionMaintenanceDisposition.PossibleCandidate);
        AssertVersion(result.Versions[3], version: 4, isLatest: false, isDraft: false, ["Preview"], ResourceVersionMaintenanceDisposition.Protected);
        AssertVersion(result.Versions[4], version: 5, isLatest: true, isDraft: true, [], ResourceVersionMaintenanceDisposition.Protected);
    }

    [Fact]
    public async Task GetHistoryAsync_MissingResourceReturnsEmptyHistory()
    {
        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "missing" });

        Assert.Equal("missing", result.ResourceId);
        Assert.Empty(result.Versions);
    }

    [Fact]
    public async Task GetHistoriesAsync_ReturnsDistinctHistoriesInFirstSeenOrder()
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "alpha", versionCount: 3);
        await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "bravo", versionCount: 2);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "alpha", "Published", [2]);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "bravo", "Preview", [1]);

        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
            new ResourceVersionHistoryBatchRequest
            {
                ResourceIds = ["bravo", "alpha", "bravo", "missing"],
            });

        Assert.Equal(TenantScope.Default, result.TenantScope);
        Assert.Equal(["bravo", "alpha", "missing"], result.Histories.Select(static history => history.ResourceId));
        Assert.Equal([1, 2], result.Histories[0].Versions.Select(static version => version.Version));
        Assert.Equal([1, 2, 3], result.Histories[1].Versions.Select(static version => version.Version));
        Assert.Empty(result.Histories[2].Versions);
        Assert.Equal(["Preview"], result.Histories[0].Versions[0].ActiveChannels);
        Assert.Equal(["Published"], result.Histories[1].Versions[1].ActiveChannels);
    }

    [Fact]
    public async Task GetHistoriesAsync_MatchesSingleResourceHistorySemantics()
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "history", versionCount: 4);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "history", "Published", [2]);
        await ResourceVersionHistoryTestFixtures.MarkAsync(provider, "history", ResourceLifecycleMarkerState.Archived);

        var service = provider.GetRequiredService<IResourceVersionHistoryService>();

        var batch = await service.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest
        {
            ResourceIds = ["history"],
        });
        var single = await service.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = "history" });

        var batchHistory = Assert.Single(batch.Histories);
        Assert.Equal(single.ResourceId, batchHistory.ResourceId);
        Assert.Equal(single.TenantScope, batchHistory.TenantScope);
        Assert.Equal(single.Versions.Select(ToComparable), batchHistory.Versions.Select(ToComparable));
    }

    [Fact]
    public async Task GetHistoriesAsync_EmptySelectionReturnsEmptyBatch()
    {
        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
            new ResourceVersionHistoryBatchRequest { ResourceIds = [] });

        Assert.Equal(TenantScope.Default, result.TenantScope);
        Assert.Empty(result.Histories);
    }

    [Fact]
    public async Task GetHistoriesAsync_MissingResourceReturnsEmptyHistory()
    {
        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
            new ResourceVersionHistoryBatchRequest { ResourceIds = ["missing"] });

        var history = Assert.Single(result.Histories);
        Assert.Equal("missing", history.ResourceId);
        Assert.Empty(history.Versions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetHistoryAsync_InvalidResourceIdThrows(string? resourceId)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
                new ResourceVersionHistoryRequest { ResourceId = resourceId }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetHistoriesAsync_InvalidResourceIdThrows(string? resourceId)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
                new ResourceVersionHistoryBatchRequest { ResourceIds = ["valid", resourceId!] }));
    }

    [Fact]
    public async Task GetHistoryAsync_NullRequestThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(null!));
    }

    [Fact]
    public async Task GetHistoriesAsync_NullRequestThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(null!));
    }

    [Fact]
    public async Task GetHistoriesAsync_NullResourceIdsThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
                new ResourceVersionHistoryBatchRequest()));
    }

    [Fact]
    public async Task GetHistoryAsync_WhenCustomVersionReaderIsActiveUsesMatchingActivationReader()
    {
        var services = new ServiceCollection().AddAsterCore();
        services.AddSingleton<CustomHistoryProvider>();
        services.AddSingleton<IResourceVersionReader>(sp => sp.GetRequiredService<CustomHistoryProvider>());

        using var customProvider = services.BuildServiceProvider();

        var result = await customProvider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "custom" });

        var summary = Assert.Single(result.Versions);
        Assert.False(summary.IsDraft);
        Assert.True(summary.IsProtectedFromPruning);
        Assert.Equal(ResourceVersionMaintenanceDisposition.Protected, summary.MaintenanceDisposition);
        Assert.Equal(["External"], summary.ActiveChannels);
    }

    [Fact]
    public void GetHistoryAsync_WhenCustomVersionReaderCannotReadActivationStateFailsFast()
    {
        var services = new ServiceCollection().AddAsterCore();
        services.AddSingleton<IResourceVersionReader, VersionOnlyReader>();

        using var customProvider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => customProvider.GetRequiredService<IResourceVersionHistoryService>());
        Assert.Contains(nameof(IResourceActivationStateReader), exception.Message, StringComparison.Ordinal);
    }

    private static void AssertVersion(
        ResourceVersionSummary summary,
        int version,
        bool isLatest,
        bool isDraft,
        IReadOnlyList<string> activeChannels,
        ResourceVersionMaintenanceDisposition disposition)
    {
        Assert.Equal(version, summary.Version);
        Assert.Equal($"default-history-{version}", summary.ResourceVersionId);
        Assert.Equal("Product", summary.DefinitionId);
        Assert.Equal(1, summary.DefinitionVersion);
        Assert.Equal(isLatest, summary.IsLatest);
        Assert.Equal(isDraft, summary.IsDraft);
        Assert.Equal(activeChannels, summary.ActiveChannels);
        Assert.Equal(disposition, summary.MaintenanceDisposition);
        Assert.Equal(disposition == ResourceVersionMaintenanceDisposition.Protected, summary.IsProtectedFromPruning);
    }

    private static object ToComparable(ResourceVersionSummary summary) => new
    {
        summary.ResourceVersionId,
        summary.Version,
        summary.DefinitionId,
        summary.DefinitionVersion,
        summary.Created,
        summary.IsLatest,
        summary.IsDraft,
        ActiveChannels = string.Join("|", summary.ActiveChannels),
        summary.LifecycleState,
        summary.IsProtectedFromPruning,
        summary.MaintenanceDisposition,
    };

    private sealed class CustomHistoryProvider : IResourceVersionReader, IResourceActivationStateReader
    {
        private static readonly Resource CustomResource = new()
        {
            TenantScope = TenantScope.Default,
            ResourceId = "custom",
            Id = "custom-history-1",
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = 1,
            Created = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc),
        };

        private static readonly ActivationState CustomActivation = new()
        {
            TenantScope = TenantScope.Default,
            ResourceId = "custom",
            Channel = "External",
            ActiveVersions = [1],
            LastUpdated = new DateTime(2026, 5, 29, 12, 5, 0, DateTimeKind.Utc),
        };

        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            var selection = request.GetResourceIdSelection();
            var resources = selection.Matches(CustomResource.ResourceId)
                ? new[] { CustomResource }
                : [];

            return ValueTask.FromResult<IEnumerable<Resource>>(resources);
        }

        public ValueTask<IReadOnlyList<ActivationState>> ReadActivationStatesAsync(
            IEnumerable<string> resourceIds,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default)
        {
            var ids = resourceIds.ToHashSet(StringComparer.Ordinal);
            var states = ids.Contains(CustomActivation.ResourceId)
                ? new[] { CustomActivation }
                : [];

            return ValueTask.FromResult<IReadOnlyList<ActivationState>>(states);
        }
    }

    private sealed class VersionOnlyReader : IResourceVersionReader
    {
        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<Resource>>([]);
    }
}
