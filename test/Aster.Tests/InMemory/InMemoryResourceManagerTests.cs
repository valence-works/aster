using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aster.Tests.InMemory;

public sealed class InMemoryResourceManagerTests
{
    private static (InMemoryResourceManager Manager, InMemoryResourceStore Store) CreateManager(
        IResourceDefinitionStore? definitionStore = null)
    {
        var store = new InMemoryResourceStore();
        var defStore = definitionStore ?? Substitute.For<IResourceDefinitionStore>();
        var idGen = new Aster.Core.Services.GuidIdentityGenerator();
        var manager = new InMemoryResourceManager(
            store, defStore, idGen,
            NullLogger<InMemoryResourceManager>.Instance);
        return (manager, store);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_ProducesVersionOne()
    {
        // Arrange
        var (manager, _) = CreateManager();

        // Act
        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Title"] = "Gadget" }
        });

        // Assert
        Assert.Equal(1, resource.Version);
        Assert.NotEmpty(resource.ResourceId);
        Assert.NotEmpty(resource.Id);
        Assert.Equal("Product", resource.DefinitionId);
        Assert.Equal("Gadget", resource.Aspects["Title"]);
    }

    [Fact]
    public async Task CreateAsync_CallerSuppliedResourceId_UsesSuppliedId()
    {
        // Arrange
        var (manager, _) = CreateManager();

        // Act
        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = "my-product-001"
        });

        // Assert
        Assert.Equal("my-product-001", resource.ResourceId);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCallerSuppliedId_ThrowsDuplicateResourceIdException()
    {
        // Arrange
        var (manager, _) = CreateManager();
        await manager.CreateAsync("Product", new CreateResourceRequest { ResourceId = "dup-id" });

        // Act & Assert
        await Assert.ThrowsAsync<DuplicateResourceIdException>(() =>
            manager.CreateAsync("Product", new CreateResourceRequest { ResourceId = "dup-id" }).AsTask());
    }

    [Fact]
    public async Task CreateAsync_SingletonDefinitionSecondInstance_ThrowsSingletonViolationException()
    {
        // Arrange
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Config")
            .WithSingleton()
            .Build();
        definition = definition with { Version = 1 };

        var defStore = Substitute.For<IResourceDefinitionStore>();
        defStore.GetDefinitionAsync("Config").Returns(definition);

        var (manager, _) = CreateManager(defStore);

        // First instance — should succeed
        await manager.CreateAsync("Config", new CreateResourceRequest());

        // Act & Assert — second instance must throw
        await Assert.ThrowsAsync<SingletonViolationException>(() =>
            manager.CreateAsync("Config", new CreateResourceRequest()).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // UpdateAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ValidBaseVersion_IncrementsVersion()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Title"] = "Gadget" }
        });

        // Act
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v1.Version,
            AspectUpdates = new Dictionary<string, object> { ["Title"] = "Super Gadget Pro" }
        });

        // Assert
        Assert.Equal(2, v2.Version);
        Assert.Equal("Super Gadget Pro", v2.Aspects["Title"]);
        Assert.Equal(v1.ResourceId, v2.ResourceId); // same logical ID
        Assert.NotEqual(v1.Id, v2.Id);              // different version-specific ID
    }

    [Fact]
    public async Task UpdateAsync_WrongBaseVersion_ThrowsConcurrencyException()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());

        // Act & Assert — pass BaseVersion = 99, which doesn't match current latest (1)
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 99 }).AsTask());
    }

    [Fact]
    public async Task UpdateAsync_MultipleUpdates_VersionsAreImmutable()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Title"] = "V1 Title" }
        });

        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v1.Version,
            AspectUpdates = new Dictionary<string, object> { ["Title"] = "V2 Title" }
        });

        // Act
        var v3 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v2.Version,
            AspectUpdates = new Dictionary<string, object> { ["Title"] = "V3 Title" }
        });

        // Assert — retrieve v1 and confirm it's unchanged
        var v1Retrieved = await manager.GetVersionAsync(v1.ResourceId, 1);
        Assert.NotNull(v1Retrieved);
        Assert.Equal("V1 Title", v1Retrieved.Aspects["Title"]);
        Assert.Equal(3, v3.Version);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetVersion / GetVersions / GetLatestVersion
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestVersionAsync_AfterUpdates_ReturnsLatest()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });

        // Act
        var latest = await manager.GetLatestVersionAsync(v1.ResourceId);

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
    }

    [Fact]
    public async Task GetVersionsAsync_ReturnsAllVersionsInOrder()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 1 });
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest { BaseVersion = 2 });

        // Act
        var versions = (await manager.GetVersionsAsync(v1.ResourceId)).ToList();

        // Assert
        Assert.Equal(3, versions.Count);
        Assert.Equal([1, 2, 3], versions.Select(r => r.Version).ToList());
    }

    [Fact]
    public async Task GetVersionAsync_NonExistentVersion_ReturnsNull()
    {
        // Arrange
        var (manager, _) = CreateManager();
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());

        // Act
        var result = await manager.GetVersionAsync(v1.ResourceId, 99);

        // Assert
        Assert.Null(result);
    }
}
