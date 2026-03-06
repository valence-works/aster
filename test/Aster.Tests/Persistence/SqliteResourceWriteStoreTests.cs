using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests for <see cref="Aster.Persistence.Sqlite.Persistence.SqliteResourceWriteStore"/>.
/// Covers append-only writes, version retrieval, latest version lookup,
/// and duplicate ResourceId detection.
/// </summary>
public sealed class SqliteResourceWriteStoreTests : IDisposable
{
    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private static Resource MakeResource(
        string resourceId = "res-1",
        int version = 1,
        string definitionId = "Product",
        Dictionary<string, object>? aspects = null)
    {
        return new Resource
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString(),
            DefinitionId = definitionId,
            Version = version,
            Created = DateTime.UtcNow,
            Owner = "test-user",
            Aspects = aspects ?? new Dictionary<string, object>()
        };
    }

    [Fact]
    public async Task SaveVersionAsync_V1_PersistsResource()
    {
        var store = fixture.WriteStore;
        var resource = MakeResource();

        var saved = await store.SaveVersionAsync(resource);

        Assert.Equal(resource.ResourceId, saved.ResourceId);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task SaveVersionAsync_MultipleVersions_AppendOnly()
    {
        var store = fixture.WriteStore;

        var v1 = MakeResource(version: 1);
        var v2 = MakeResource(version: 2);

        await store.SaveVersionAsync(v1);
        await store.SaveVersionAsync(v2);

        var retrieved = (await store.GetVersionsAsync("res-1")).ToList();

        Assert.Equal(2, retrieved.Count);
        Assert.Equal(1, retrieved[0].Version);
        Assert.Equal(2, retrieved[1].Version);
    }

    [Fact]
    public async Task SaveVersionAsync_DuplicateNonV1Version_ThrowsConcurrencyException()
    {
        var store = fixture.WriteStore;

        await store.SaveVersionAsync(MakeResource(version: 1));
        await store.SaveVersionAsync(MakeResource(version: 2));

        // Attempt to insert same (ResourceId, Version=2) again
        var duplicate = MakeResource(version: 2);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.SaveVersionAsync(duplicate).AsTask());
    }

    [Fact]
    public async Task SaveVersionAsync_DuplicateResourceId_V1_ThrowsDuplicateResourceIdException()
    {
        var store = fixture.WriteStore;

        var v1 = MakeResource(resourceId: "dup-res", version: 1);
        await store.SaveVersionAsync(v1);

        // Try to create another V1 with the same ResourceId
        var duplicate = MakeResource(resourceId: "dup-res", version: 1);

        await Assert.ThrowsAsync<DuplicateResourceIdException>(() =>
            store.SaveVersionAsync(duplicate).AsTask());
    }

    [Fact]
    public async Task GetVersionAsync_Existing_ReturnsResource()
    {
        var store = fixture.WriteStore;
        var resource = MakeResource();
        await store.SaveVersionAsync(resource);

        var result = await store.GetVersionAsync("res-1", 1);

        Assert.NotNull(result);
        Assert.Equal("res-1", result.ResourceId);
        Assert.Equal(1, result.Version);
        Assert.Equal("Product", result.DefinitionId);
        Assert.Equal("test-user", result.Owner);
    }

    [Fact]
    public async Task GetVersionAsync_NonExistent_ReturnsNull()
    {
        var result = await fixture.WriteStore.GetVersionAsync("no-such-id", 1);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestVersionAsync_ReturnsHighestVersion()
    {
        var store = fixture.WriteStore;

        await store.SaveVersionAsync(MakeResource(version: 1));
        await store.SaveVersionAsync(MakeResource(version: 2));
        await store.SaveVersionAsync(MakeResource(version: 3));

        var latest = await store.GetLatestVersionAsync("res-1");

        Assert.NotNull(latest);
        Assert.Equal(3, latest.Version);
    }

    [Fact]
    public async Task GetLatestVersionAsync_NonExistent_ReturnsNull()
    {
        var result = await fixture.WriteStore.GetLatestVersionAsync("no-resource");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMaxVersionAsync_ReturnsMaxVersion()
    {
        var store = fixture.WriteStore;

        await store.SaveVersionAsync(MakeResource(version: 1));
        await store.SaveVersionAsync(MakeResource(version: 2));

        var max = await store.GetMaxVersionAsync("res-1");
        Assert.Equal(2, max);
    }

    [Fact]
    public async Task GetMaxVersionAsync_NoVersions_ReturnsZero()
    {
        var max = await fixture.WriteStore.GetMaxVersionAsync("no-id");
        Assert.Equal(0, max);
    }

    [Fact]
    public async Task AnyResourceExistsForDefinitionAsync_Exists_ReturnsTrue()
    {
        var store = fixture.WriteStore;
        await store.SaveVersionAsync(MakeResource(definitionId: "Singleton"));

        var exists = await store.AnyResourceExistsForDefinitionAsync("Singleton");
        Assert.True(exists);
    }

    [Fact]
    public async Task AnyResourceExistsForDefinitionAsync_NotExists_ReturnsFalse()
    {
        var exists = await fixture.WriteStore.AnyResourceExistsForDefinitionAsync("NoDef");
        Assert.False(exists);
    }

    [Fact]
    public async Task SaveVersionAsync_WithAspects_PersistsAndRoundTrips()
    {
        var store = fixture.WriteStore;

        var aspects = new Dictionary<string, object>
        {
            { "TitleAspect", new { Title = "Test Product" } },
            { "PriceAspect", new { Amount = 49.99, Currency = "EUR" } }
        };
        var resource = MakeResource(aspects: aspects);

        await store.SaveVersionAsync(resource);
        var retrieved = await store.GetVersionAsync("res-1", 1);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Aspects.Count);
        Assert.True(retrieved.Aspects.ContainsKey("TitleAspect"));
        Assert.True(retrieved.Aspects.ContainsKey("PriceAspect"));
    }

    [Fact]
    public async Task SaveVersionAsync_WithOptionalFields_PersistsNulls()
    {
        var store = fixture.WriteStore;

        var resource = new Resource
        {
            ResourceId = "nullable-res",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Owner = null,
            Hash = null,
            DefinitionVersion = null,
        };

        await store.SaveVersionAsync(resource);
        var result = await store.GetVersionAsync("nullable-res", 1);

        Assert.NotNull(result);
        Assert.Null(result.Owner);
        Assert.Null(result.Hash);
        Assert.Null(result.DefinitionVersion);
    }
}
