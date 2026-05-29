using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aster.Tests.InMemory;

public sealed class InMemoryActivationTests
{
    private static (InMemoryResourceManager Manager, InMemoryResourceStore Store) CreateManager()
    {
        var store = new InMemoryResourceStore();
        var defStore = Substitute.For<IResourceDefinitionStore>();
        var manager = new InMemoryResourceManager(
            store, defStore, new GuidIdentityGenerator(),
            NullLogger<InMemoryResourceManager>.Instance);
        return (manager, store);
    }

    private static async Task<string> SeedResource(InMemoryResourceManager manager, string definitionId = "Product")
    {
        var resource = await manager.CreateAsync(definitionId, new CreateResourceRequest());
        return resource.ResourceId;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ActivateAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivateAsync_LatestVersion_ActivatesSuccessfully()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var resourceId = await SeedResource(manager);

        // Act
        await manager.ActivateAsync(resourceId, 1, "Published");
        var active = (await manager.GetActiveVersionsAsync(resourceId, "Published")).ToList();

        // Assert
        Assert.Single(active);
        Assert.Equal(1, active[0].Version);
    }

    [Fact]
    public async Task ActivateAsync_NonExistentVersion_ThrowsVersionNotFoundException()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var resourceId = await SeedResource(manager);

        // Act & Assert
        await Assert.ThrowsAsync<VersionNotFoundException>(() =>
            manager.ActivateAsync(resourceId, 99, "Published").AsTask());
    }

    [Fact]
    public async Task ActivateAsync_HistoricalVersion_ActivatesWhileLatestRemainsUnchanged()
    {
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });

        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Published");

        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();
        var latest = await manager.GetLatestVersionAsync(v1.ResourceId);

        Assert.Single(active);
        Assert.Equal(v1.Version, active[0].Version);
        Assert.NotNull(latest);
        Assert.Equal(v2.Version, latest.Version);
    }

    [Fact]
    public async Task ActivateAsync_SingleActive_HistoricalVersionReplacesPreviousVersion()
    {
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });
        await manager.ActivateAsync(v1.ResourceId, v2.Version, "Published", allowMultipleActive: false);

        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Published", allowMultipleActive: false);
        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();

        Assert.Single(active);
        Assert.Equal(v1.Version, active[0].Version);
    }

    [Fact]
    public async Task ActivateAsync_MultiActive_HistoricalVersionAppendsInDeterministicOrder()
    {
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });
        await manager.ActivateAsync(v1.ResourceId, v2.Version, "Preview", allowMultipleActive: true);

        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Preview", allowMultipleActive: true);
        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Preview")).ToList();

        Assert.Equal([1, 2], active.Select(r => r.Version).ToList());
    }

    [Fact]
    public async Task DefaultResourceManager_HistoricalVersion_ActivatesThroughProviderBackedPath()
    {
        await using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        var manager = provider.GetRequiredService<IResourceManager>();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });
        await manager.ActivateAsync(v1.ResourceId, v2.Version, "Published");

        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Published");

        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();
        var latest = await manager.GetLatestVersionAsync(v1.ResourceId);
        Assert.Single(active);
        Assert.Equal(v1.Version, active[0].Version);
        Assert.NotNull(latest);
        Assert.Equal(v2.Version, latest.Version);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DeactivateAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_ActiveVersion_RemovesFromChannel()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var resourceId = await SeedResource(manager);
        await manager.ActivateAsync(resourceId, 1, "Published");

        // Act
        await manager.DeactivateAsync(resourceId, 1, "Published");
        var active = (await manager.GetActiveVersionsAsync(resourceId, "Published")).ToList();

        // Assert
        Assert.Empty(active);
    }

    [Fact]
    public async Task DeactivateAsync_NonActiveVersion_DoesNotThrow()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var resourceId = await SeedResource(manager);

        // Act & Assert — deactivating a version that was never activated is a no-op
        await manager.DeactivateAsync(resourceId, 1, "Published");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Channel isolation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveVersionsAsync_DifferentChannels_AreIsolated()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var resourceId = await SeedResource(manager);
        await manager.ActivateAsync(resourceId, 1, "Published");

        // Act — "Preview" channel has not been activated
        var preview = (await manager.GetActiveVersionsAsync(resourceId, "Preview")).ToList();

        // Assert
        Assert.Empty(preview);
    }
}
