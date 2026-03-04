using Aster.Core.Definitions;
using Aster.Core.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.InMemory;

public sealed class InMemoryResourceDefinitionStoreTests
{
    private static InMemoryResourceDefinitionStore CreateStore() =>
        new(NullLogger<InMemoryResourceDefinitionStore>.Instance);

    private static ResourceDefinitionBuilder ProductBuilder() =>
        new ResourceDefinitionBuilder().WithDefinitionId("Product");

    [Fact]
    public async Task RegisterDefinitionAsync_FirstVersion_AssignsVersionOne()
    {
        // Arrange
        var store = CreateStore();
        var definition = ProductBuilder().Build();

        // Act
        await store.RegisterDefinitionAsync(definition);
        var result = await store.GetDefinitionAsync("Product");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal("Product", result.DefinitionId);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_SecondVersion_AutoIncrementsVersion()
    {
        // Arrange
        var store = CreateStore();

        // Act
        await store.RegisterDefinitionAsync(ProductBuilder().Build());
        await store.RegisterDefinitionAsync(ProductBuilder().Build());
        var latest = await store.GetDefinitionAsync("Product");

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
    }

    [Fact]
    public async Task GetDefinitionAsync_NonExistentId_ReturnsNull()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var result = await store.GetDefinitionAsync("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefinitionVersionAsync_ExistingVersion_ReturnsCorrectSnapshot()
    {
        // Arrange
        var store = CreateStore();
        await store.RegisterDefinitionAsync(ProductBuilder().Build()); // v1
        await store.RegisterDefinitionAsync(ProductBuilder().Build()); // v2

        // Act
        var v1 = await store.GetDefinitionVersionAsync("Product", 1);
        var v2 = await store.GetDefinitionVersionAsync("Product", 2);

        // Assert
        Assert.NotNull(v1);
        Assert.Equal(1, v1.Version);
        Assert.NotNull(v2);
        Assert.Equal(2, v2.Version);
    }

    [Fact]
    public async Task GetDefinitionVersionAsync_NonExistentVersion_ReturnsNull()
    {
        // Arrange
        var store = CreateStore();
        await store.RegisterDefinitionAsync(ProductBuilder().Build());

        // Act
        var result = await store.GetDefinitionVersionAsync("Product", 99);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ListDefinitionsAsync_MultipleDefinitions_ReturnsLatestPerDefinitionId()
    {
        // Arrange
        var store = CreateStore();

        // Register two versions of Product and one of Order
        await store.RegisterDefinitionAsync(ProductBuilder().Build());
        await store.RegisterDefinitionAsync(ProductBuilder().Build()); // v2
        await store.RegisterDefinitionAsync(new ResourceDefinitionBuilder().WithDefinitionId("Order").Build()); // v1

        // Act
        var list = (await store.ListDefinitionsAsync()).ToList();

        // Assert — one entry per distinct DefinitionId, each at its latest version
        Assert.Equal(2, list.Count);

        var product = list.Single(d => d.DefinitionId == "Product");
        Assert.Equal(2, product.Version);

        var order = list.Single(d => d.DefinitionId == "Order");
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_DoesNotMutateExistingVersions()
    {
        // Arrange
        var store = CreateStore();
        await store.RegisterDefinitionAsync(ProductBuilder().Build());

        var v1Snapshot = await store.GetDefinitionVersionAsync("Product", 1);
        Assert.NotNull(v1Snapshot);

        // Act — register a second version
        await store.RegisterDefinitionAsync(ProductBuilder().Build());

        // Assert — v1 is unchanged
        var v1Again = await store.GetDefinitionVersionAsync("Product", 1);
        Assert.NotNull(v1Again);
        Assert.Equal(1, v1Again.Version);
        Assert.Equal(v1Snapshot.Id, v1Again.Id);
    }
}
