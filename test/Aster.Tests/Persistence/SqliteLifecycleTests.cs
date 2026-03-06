using Aster.Core.Models.Instances;

namespace Aster.Tests.Persistence;

/// <summary>
/// Baseline provider lifecycle unit test covering the full
/// create → multi-version-update → activate → deactivate → retrieve cycle via the Sqlite provider.
/// </summary>
public sealed class SqliteLifecycleTests : IDisposable
{
    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task FullLifecycle_CreateUpdateActivateDeactivateRetrieve()
    {
        var defStore = fixture.DefinitionStore;
        var store = fixture.WriteStore;

        // ── Step 1: Register definition ──────────────────────────────────────
        var definition = new Aster.Core.Definitions.ResourceDefinitionBuilder()
            .WithDefinitionId("LifecycleProduct")
            .Build();
        await defStore.RegisterDefinitionAsync(definition);

        var storedDef = await defStore.GetDefinitionAsync("LifecycleProduct");
        Assert.NotNull(storedDef);
        Assert.Equal(1, storedDef.Version);

        // ── Step 2: Create resource V1 ───────────────────────────────────────
        var v1 = new Resource
        {
            ResourceId = "lifecycle-res",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "LifecycleProduct",
            DefinitionVersion = 1,
            Version = 1,
            Created = DateTime.UtcNow,
            Owner = "lifecycle-owner",
            Aspects = new Dictionary<string, object>
            {
                { "Title", new { Name = "Widget" } },
            },
        };
        await store.SaveVersionAsync(v1);

        var retrievedV1 = await store.GetVersionAsync("lifecycle-res", 1);
        Assert.NotNull(retrievedV1);
        Assert.Equal("lifecycle-res", retrievedV1.ResourceId);
        Assert.Equal(1, retrievedV1.Version);
        Assert.Equal("lifecycle-owner", retrievedV1.Owner);
        Assert.True(retrievedV1.Aspects.ContainsKey("Title"));

        // ── Step 3: Update to V2 ────────────────────────────────────────────
        var v2 = new Resource
        {
            ResourceId = "lifecycle-res",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "LifecycleProduct",
            DefinitionVersion = 1,
            Version = 2,
            Created = DateTime.UtcNow,
            Owner = "lifecycle-owner",
            Aspects = new Dictionary<string, object>
            {
                { "Title", new { Name = "Widget Pro" } },
            },
        };
        await store.SaveVersionAsync(v2);

        // ── Step 4: Update to V3 ────────────────────────────────────────────
        var v3 = new Resource
        {
            ResourceId = "lifecycle-res",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "LifecycleProduct",
            DefinitionVersion = 1,
            Version = 3,
            Created = DateTime.UtcNow,
            Owner = "lifecycle-owner",
            Aspects = new Dictionary<string, object>
            {
                { "Title", new { Name = "Widget Ultra" } },
                { "Price", new { Amount = 199.99, Currency = "USD" } },
            },
        };
        await store.SaveVersionAsync(v3);

        // Verify all versions exist
        var allVersions = (await store.GetVersionsAsync("lifecycle-res")).ToList();
        Assert.Equal(3, allVersions.Count);

        var latest = await store.GetLatestVersionAsync("lifecycle-res");
        Assert.NotNull(latest);
        Assert.Equal(3, latest.Version);

        // ── Step 5: Activate V3 in Published ────────────────────────────────
        await store.UpdateActivationAsync("lifecycle-res", "Published", new ActivationState
        {
            ResourceId = "lifecycle-res",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [3],
            LastUpdated = DateTime.UtcNow,
        });

        var activePublished = (await store.GetActiveVersionsAsync("lifecycle-res", "Published")).ToList();
        Assert.Single(activePublished);
        Assert.Equal(3, activePublished[0].Version);
        Assert.Equal(2, activePublished[0].Aspects.Count);

        // ── Step 6: Activate V1 and V2 in Preview (MultiActive) ─────────────
        await store.UpdateActivationAsync("lifecycle-res", "Preview", new ActivationState
        {
            ResourceId = "lifecycle-res",
            Channel = "Preview",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [1, 2],
            LastUpdated = DateTime.UtcNow,
        });

        var activePreview = (await store.GetActiveVersionsAsync("lifecycle-res", "Preview")).ToList();
        Assert.Equal(2, activePreview.Count);

        // ── Step 7: Deactivate V1 from Preview ─────────────────────────────
        await store.UpdateActivationAsync("lifecycle-res", "Preview", new ActivationState
        {
            ResourceId = "lifecycle-res",
            Channel = "Preview",
            Mode = ChannelMode.MultiActive,
            ActiveVersions = [2], // removed V1
            LastUpdated = DateTime.UtcNow,
        });

        var previewAfterDeactivate = (await store.GetActiveVersionsAsync("lifecycle-res", "Preview")).ToList();
        Assert.Single(previewAfterDeactivate);
        Assert.Equal(2, previewAfterDeactivate[0].Version);

        // ── Step 8: Verify full retrieval ───────────────────────────────────
        // All 3 versions still exist (append-only)
        var finalVersions = (await store.GetVersionsAsync("lifecycle-res")).ToList();
        Assert.Equal(3, finalVersions.Count);

        // V1 can still be retrieved by version
        var v1Again = await store.GetVersionAsync("lifecycle-res", 1);
        Assert.NotNull(v1Again);
        Assert.Equal("lifecycle-owner", v1Again.Owner);

        // Published still has V3
        var pubState = await store.GetActivationStateAsync("lifecycle-res", "Published");
        Assert.NotNull(pubState);
        Assert.Equal(ChannelMode.SingleActive, pubState.Mode);
        Assert.Single(pubState.ActiveVersions);
        Assert.Equal(3, pubState.ActiveVersions[0]);
    }
}
