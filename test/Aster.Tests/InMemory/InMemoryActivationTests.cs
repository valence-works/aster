using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Services;
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
        await manager.ActivateAsync(resourceId, 1, "Published", ChannelMode.SingleActive);
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
    public async Task ActivateAsync_NonLatestVersion_ThrowsConcurrencyException()
    {
        // Arrange — create V1, update to V2, then try to activate stale V1
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });

        // Act & Assert — V2 is latest; activating V1 is a concurrency conflict
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            manager.ActivateAsync(v1.ResourceId, 1, "Published").AsTask());
    }

    [Fact]
    public async Task ActivateAsync_NoMode_FirstActivation_ThrowsValidationException()
    {
        // Arrange — first activation must supply an explicit ChannelMode
        var (manager, _) = CreateManager();
        var resourceId = await SeedResource(manager);

        // Act & Assert — omitting mode on first activation for a channel returns ValidationFailed
        await Assert.ThrowsAsync<ValidationException>(() =>
            manager.ActivateAsync(resourceId, 1, "Published").AsTask());
    }

    [Fact]
    public async Task ActivateAsync_NoMode_SubsequentActivation_ReusesStoredMode()
    {
        // Arrange — set mode on first activation, then reuse on subsequent
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.ActivateAsync(v1.ResourceId, 1, "Published", ChannelMode.SingleActive);

        // Update to V2
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });

        // Act — activate V2 without explicit mode (should reuse SingleActive)
        await manager.ActivateAsync(v1.ResourceId, 2, "Published");
        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();

        // Assert — SingleActive means only V2 active
        Assert.Single(active);
        Assert.Equal(2, active[0].Version);
    }

    [Fact]
    public async Task ActivateAsync_SingleActive_DeactivatesPreviousVersion()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.ActivateAsync(v1.ResourceId, 1, "Published", ChannelMode.SingleActive);

        // Sanity: V1 is active
        var beforeUpdate = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();
        Assert.Single(beforeUpdate);

        // Update to V2 so V2 is now latest
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });

        // Act — activate V2 with single-active (reuses stored mode)
        await manager.ActivateAsync(v1.ResourceId, 2, "Published");
        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();

        // Assert — only V2 should be active
        Assert.Single(active);
        Assert.Equal(2, active[0].Version);
    }

    [Fact]
    public async Task ActivateAsync_MultiActive_AppendsBothVersions()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.ActivateAsync(v1.ResourceId, 1, "Preview", ChannelMode.MultiActive);

        // Update to V2 and activate too (multi-active — reuses stored mode)
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });
        await manager.ActivateAsync(v1.ResourceId, 2, "Preview");

        // Act
        var active = (await manager.GetActiveVersionsAsync(v1.ResourceId, "Preview")).ToList();

        // Assert — both V1 and V2 are active in Preview
        Assert.Equal(2, active.Count);
        Assert.Contains(active, r => r.Version == 1);
        Assert.Contains(active, r => r.Version == 2);
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
        await manager.ActivateAsync(resourceId, 1, "Published", ChannelMode.SingleActive);

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
        await manager.ActivateAsync(resourceId, 1, "Published", ChannelMode.SingleActive);

        // Act — "Preview" channel has not been activated
        var preview = (await manager.GetActiveVersionsAsync(resourceId, "Preview")).ToList();

        // Assert
        Assert.Empty(preview);
    }
}
