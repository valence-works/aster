using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests for <see cref="Aster.Persistence.Sqlite.Persistence.SqliteResourceDefinitionStore"/>.
/// Covers registration, round-trip retrieval (latest, specific version, list),
/// immutable version history, and IsSingleton flag persistence.
/// </summary>
public sealed class SqliteDefinitionStoreTests : IDisposable
{
    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private static ResourceDefinition BuildDefinition(string id = "Product", bool isSingleton = false)
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId(id);
        if (isSingleton)
        {
            // Use the record 'with' expression to set IsSingleton after building
            return builder.Build() with { IsSingleton = true };
        }

        return builder.Build();
    }

    [Fact]
    public async Task RegisterDefinitionAsync_FirstVersion_AssignsVersionOne()
    {
        var store = fixture.DefinitionStore;
        var def = BuildDefinition();

        await store.RegisterDefinitionAsync(def);
        var result = await store.GetDefinitionAsync("Product");

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal("Product", result.DefinitionId);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_SecondVersion_AutoIncrementsVersion()
    {
        var store = fixture.DefinitionStore;

        await store.RegisterDefinitionAsync(BuildDefinition());
        await store.RegisterDefinitionAsync(BuildDefinition());

        var latest = await store.GetDefinitionAsync("Product");

        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
    }

    [Fact]
    public async Task GetDefinitionAsync_NonExistent_ReturnsNull()
    {
        var result = await fixture.DefinitionStore.GetDefinitionAsync("NonExistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefinitionVersionAsync_SpecificVersion_ReturnsCorrectSnapshot()
    {
        var store = fixture.DefinitionStore;

        await store.RegisterDefinitionAsync(BuildDefinition()); // v1
        await store.RegisterDefinitionAsync(BuildDefinition()); // v2

        var v1 = await store.GetDefinitionVersionAsync("Product", 1);
        var v2 = await store.GetDefinitionVersionAsync("Product", 2);

        Assert.NotNull(v1);
        Assert.Equal(1, v1.Version);
        Assert.NotNull(v2);
        Assert.Equal(2, v2.Version);
    }

    [Fact]
    public async Task GetDefinitionVersionAsync_NonExistent_ReturnsNull()
    {
        var store = fixture.DefinitionStore;
        await store.RegisterDefinitionAsync(BuildDefinition());

        var result = await store.GetDefinitionVersionAsync("Product", 99);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListDefinitionsAsync_MultipleDefinitions_ReturnsLatestPerDefinitionId()
    {
        var store = fixture.DefinitionStore;

        await store.RegisterDefinitionAsync(BuildDefinition("Product"));
        await store.RegisterDefinitionAsync(BuildDefinition("Product")); // v2
        await store.RegisterDefinitionAsync(BuildDefinition("Order"));

        var list = (await store.ListDefinitionsAsync()).ToList();

        Assert.Equal(2, list.Count);

        var product = list.Single(d => d.DefinitionId == "Product");
        Assert.Equal(2, product.Version);

        var order = list.Single(d => d.DefinitionId == "Order");
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_DoesNotMutateExistingVersions()
    {
        var store = fixture.DefinitionStore;
        await store.RegisterDefinitionAsync(BuildDefinition());

        var v1Snapshot = await store.GetDefinitionVersionAsync("Product", 1);
        Assert.NotNull(v1Snapshot);

        await store.RegisterDefinitionAsync(BuildDefinition());

        var v1Again = await store.GetDefinitionVersionAsync("Product", 1);
        Assert.NotNull(v1Again);
        Assert.Equal(1, v1Again.Version);
        Assert.Equal(v1Snapshot.Id, v1Again.Id);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_IsSingleton_PersistsFlag()
    {
        var store = fixture.DefinitionStore;
        await store.RegisterDefinitionAsync(BuildDefinition("SingletonDef", isSingleton: true));

        var result = await store.GetDefinitionAsync("SingletonDef");

        Assert.NotNull(result);
        Assert.True(result.IsSingleton);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_WithAspects_PersistsAspectDefinitions()
    {
        var store = fixture.DefinitionStore;

        var def = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect<TestTitleAspect>()
            .Build();

        await store.RegisterDefinitionAsync(def);
        var result = await store.GetDefinitionAsync("Product");

        Assert.NotNull(result);
        Assert.Single(result.AspectDefinitions);
        Assert.True(result.AspectDefinitions.ContainsKey(nameof(TestTitleAspect)));
    }

    [Fact]
    public async Task ListDefinitionsAsync_Empty_ReturnsEmpty()
    {
        var emptyFixture = new SqliteTestFixture();
        try
        {
            var list = (await emptyFixture.DefinitionStore.ListDefinitionsAsync()).ToList();
            Assert.Empty(list);
        }
        finally
        {
            emptyFixture.Dispose();
        }
    }

    // Test aspect types
    private sealed record TestTitleAspect(string Title);
}
