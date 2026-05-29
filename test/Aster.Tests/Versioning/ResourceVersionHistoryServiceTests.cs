using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
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

    [Fact]
    public async Task GetHistoryAsync_NullRequestThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(null!));
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
}
