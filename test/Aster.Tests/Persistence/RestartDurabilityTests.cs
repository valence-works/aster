using Aster.Core.Models.Instances;
using Aster.Persistence.Sqlite;
using Aster.Persistence.Sqlite.Persistence;
using Aster.Persistence.Sqlite.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Persistence;

/// <summary>
/// Integration test verifying all versions, activation state, and stored ChannelMode
/// survive "process restart" (i.e. fresh store instances against the same database).
/// Covers SC-001 and SC-005 evidence.
/// </summary>
public sealed class RestartDurabilityTests : IDisposable
{
    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task AllVersions_SurviveRestart()
    {
        // Phase 1: Write data with original store instances
        var store = fixture.WriteStore;

        for (var v = 1; v <= 3; v++)
        {
            await store.SaveVersionAsync(new Resource
            {
                ResourceId = "durable-res",
                Id = Guid.NewGuid().ToString(),
                DefinitionId = "Product",
                Version = v,
                Created = DateTime.UtcNow,
                Owner = $"owner-v{v}",
            });
        }

        // Activate V2 in Published (SingleActive) and V1+V3 in Preview (MultiActive)
        await store.UpdateActivationAsync("durable-res", "Published", new ActivationState
        {
            ResourceId = "durable-res",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [2],
            LastUpdated = DateTime.UtcNow,
        });

        await store.UpdateActivationAsync("durable-res", "Preview", new ActivationState
        {
            ResourceId = "durable-res",
            Channel = "Preview",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [1, 3],
            LastUpdated = DateTime.UtcNow,
        });

        // Phase 2: Create fresh store instances (simulates restart)
        var freshWriteStore = new SqliteResourceWriteStore(
            fixture.Options,
            NullLogger<SqliteResourceWriteStore>.Instance);

        // Verify all 3 versions survived
        var versions = (await freshWriteStore.GetVersionsAsync("durable-res")).ToList();
        Assert.Equal(3, versions.Count);
        Assert.Equal(1, versions[0].Version);
        Assert.Equal(2, versions[1].Version);
        Assert.Equal(3, versions[2].Version);

        // Verify latest version
        var latest = await freshWriteStore.GetLatestVersionAsync("durable-res");
        Assert.NotNull(latest);
        Assert.Equal(3, latest.Version);

        // Verify Published activation state (SingleActive, V2)
        var published = await freshWriteStore.GetActivationStateAsync("durable-res", "Published");
        Assert.NotNull(published);
        Assert.Equal(ChannelMode.SingleActive, published.Mode);
        Assert.Single(published.ActiveVersions);
        Assert.Equal(2, published.ActiveVersions[0]);

        // Verify Preview activation state (MultiActive, V1+V3)
        var preview = await freshWriteStore.GetActivationStateAsync("durable-res", "Preview");
        Assert.NotNull(preview);
        Assert.Equal(ChannelMode.MultiActive, preview.Mode);
        Assert.Equal(2, preview.ActiveVersions.Count);
        Assert.Contains(1, preview.ActiveVersions);
        Assert.Contains(3, preview.ActiveVersions);
    }

    [Fact]
    public async Task DefinitionVersions_SurviveRestart()
    {
        // Phase 1: Register definitions
        var defStore = fixture.DefinitionStore;

        var def1 = new Aster.Core.Definitions.ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build();
        await defStore.RegisterDefinitionAsync(def1);

        var def2 = new Aster.Core.Definitions.ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build();
        await defStore.RegisterDefinitionAsync(def2);

        // Phase 2: Fresh store
        var freshDefStore = new SqliteResourceDefinitionStore(
            fixture.Options,
            NullLogger<SqliteResourceDefinitionStore>.Instance);

        // Verify both versions remain
        var v1 = await freshDefStore.GetDefinitionVersionAsync("Product", 1);
        var v2 = await freshDefStore.GetDefinitionVersionAsync("Product", 2);

        Assert.NotNull(v1);
        Assert.Equal(1, v1.Version);
        Assert.NotNull(v2);
        Assert.Equal(2, v2.Version);

        // Latest should be V2
        var latest = await freshDefStore.GetDefinitionAsync("Product");
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
    }

    [Fact]
    public async Task FullLifecycle_SurvivesRestart()
    {
        // Phase 1: Full create → version → activate cycle
        var defStore = fixture.DefinitionStore;
        var store = fixture.WriteStore;

        await defStore.RegisterDefinitionAsync(
            new Aster.Core.Definitions.ResourceDefinitionBuilder()
                .WithDefinitionId("Product")
                .Build());

        await store.SaveVersionAsync(new Resource
        {
            ResourceId = "full-test",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object> { { "Title", new { Name = "Widget" } } },
        });

        await store.SaveVersionAsync(new Resource
        {
            ResourceId = "full-test",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 2,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object> { { "Title", new { Name = "Widget Pro" } } },
        });

        await store.UpdateActivationAsync("full-test", "Published", new ActivationState
        {
            ResourceId = "full-test",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [2],
            LastUpdated = DateTime.UtcNow,
        });

        // Phase 2: New instances
        var freshStore = new SqliteResourceWriteStore(
            fixture.Options,
            NullLogger<SqliteResourceWriteStore>.Instance);

        var active = (await freshStore.GetActiveVersionsAsync("full-test", "Published")).ToList();
        Assert.Single(active);
        Assert.Equal(2, active[0].Version);

        // Verify aspect data survived
        Assert.True(active[0].Aspects.ContainsKey("Title"));
    }
}
