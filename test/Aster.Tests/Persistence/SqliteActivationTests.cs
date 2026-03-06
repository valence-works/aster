using Aster.Core.Models.Instances;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests for <see cref="Aster.Persistence.Sqlite.Persistence.SqliteResourceWriteStore"/> activation support.
/// Covers ActivationRecord persistence, durable ChannelMode, SingleActive/MultiActive enforcement.
/// </summary>
public sealed class SqliteActivationTests : IDisposable
{
    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private async Task SeedResource(string resourceId = "res-1", int versions = 1)
    {
        for (var v = 1; v <= versions; v++)
        {
            await fixture.WriteStore.SaveVersionAsync(new Resource
            {
                ResourceId = resourceId,
                Id = Guid.NewGuid().ToString(),
                DefinitionId = "Product",
                Version = v,
                Created = DateTime.UtcNow,
            });
        }
    }

    [Fact]
    public async Task UpdateActivationAsync_SingleActive_PersistsState()
    {
        await SeedResource();
        var store = fixture.WriteStore;

        var state = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        };

        var result = await store.UpdateActivationAsync("res-1", "Published", state);

        Assert.Equal("res-1", result.ResourceId);
        Assert.Equal("Published", result.Channel);
        Assert.Equal(ChannelMode.SingleActive, result.Mode);
        Assert.Single(result.ActiveVersions);
    }

    [Fact]
    public async Task GetActivationStateAsync_RoundTrips()
    {
        await SeedResource();
        var store = fixture.WriteStore;

        var state = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Preview",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Preview", state);

        var retrieved = await store.GetActivationStateAsync("res-1", "Preview");

        Assert.NotNull(retrieved);
        Assert.Equal("res-1", retrieved.ResourceId);
        Assert.Equal("Preview", retrieved.Channel);
        Assert.Equal(ChannelMode.MultiActive, retrieved.Mode);
        Assert.Single(retrieved.ActiveVersions);
        Assert.Equal(1, retrieved.ActiveVersions[0]);
    }

    [Fact]
    public async Task GetActivationStateAsync_NonExistent_ReturnsNull()
    {
        var result = await fixture.WriteStore.GetActivationStateAsync("no-id", "Published");
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateActivationAsync_Upsert_UpdatesExistingRecord()
    {
        await SeedResource(versions: 2);
        var store = fixture.WriteStore;

        // First activation: V1 in SingleActive
        var state1 = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", state1);

        // Second activation: switch to V2 (still SingleActive)
        var state2 = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [2],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", state2);

        var retrieved = await store.GetActivationStateAsync("res-1", "Published");

        Assert.NotNull(retrieved);
        Assert.Single(retrieved.ActiveVersions);
        Assert.Equal(2, retrieved.ActiveVersions[0]);
    }

    [Fact]
    public async Task UpdateActivationAsync_MultiActive_PersistsMultipleVersions()
    {
        await SeedResource(versions: 3);
        var store = fixture.WriteStore;

        var state = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Preview",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [1, 2, 3],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Preview", state);

        var retrieved = await store.GetActivationStateAsync("res-1", "Preview");

        Assert.NotNull(retrieved);
        Assert.Equal(3, retrieved.ActiveVersions.Count);
        Assert.Equal(ChannelMode.MultiActive, retrieved.Mode);
    }

    [Fact]
    public async Task GetActiveVersionsAsync_ReturnsFullResourceVersions()
    {
        await SeedResource(versions: 2);
        var store = fixture.WriteStore;

        var state = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [1, 2],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", state);

        var active = (await store.GetActiveVersionsAsync("res-1", "Published")).ToList();

        Assert.Equal(2, active.Count);
        Assert.All(active, r => Assert.Equal("res-1", r.ResourceId));
        Assert.Contains(active, r => r.Version == 1);
        Assert.Contains(active, r => r.Version == 2);
    }

    [Fact]
    public async Task GetActiveVersionsAsync_NoActivation_ReturnsEmpty()
    {
        var active = (await fixture.WriteStore.GetActiveVersionsAsync("no-id", "Published")).ToList();
        Assert.Empty(active);
    }

    [Fact]
    public async Task ChannelIsolation_DifferentChannels_AreIndependent()
    {
        await SeedResource(versions: 2);
        var store = fixture.WriteStore;

        var pubState = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", pubState);

        var preState = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Preview",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [1, 2],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Preview", preState);

        var published = await store.GetActivationStateAsync("res-1", "Published");
        var preview = await store.GetActivationStateAsync("res-1", "Preview");

        Assert.NotNull(published);
        Assert.Single(published.ActiveVersions);
        Assert.Equal(ChannelMode.SingleActive, published.Mode);

        Assert.NotNull(preview);
        Assert.Equal(2, preview.ActiveVersions.Count);
        Assert.Equal(ChannelMode.MultiActive, preview.Mode);
    }

    [Fact]
    public async Task UpdateActivationAsync_Deactivation_EmptyActiveVersions()
    {
        await SeedResource();
        var store = fixture.WriteStore;

        // Activate
        var active = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", active);

        // Deactivate
        var deactivated = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", deactivated);

        var retrieved = await store.GetActivationStateAsync("res-1", "Published");

        Assert.NotNull(retrieved);
        Assert.Empty(retrieved.ActiveVersions);
    }

    [Fact]
    public async Task UpdateActivationAsync_ChannelMode_PersistedDurably()
    {
        await SeedResource();
        var store = fixture.WriteStore;

        var state = new ActivationState
        {
            ResourceId = "res-1",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        };
        await store.UpdateActivationAsync("res-1", "Published", state);

        // Read from a fresh store to verify durability
        var freshStore = new Aster.Persistence.Sqlite.Persistence.SqliteResourceWriteStore(
            fixture.Options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Aster.Persistence.Sqlite.Persistence.SqliteResourceWriteStore>.Instance);

        var retrieved = await freshStore.GetActivationStateAsync("res-1", "Published");

        Assert.NotNull(retrieved);
        Assert.Equal(ChannelMode.SingleActive, retrieved.Mode);
    }
}
