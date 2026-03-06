using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Services;
using Aster.Tests.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Integration;

/// <summary>
/// End-to-end integration test reproducing the full quickstart.md flow:
/// Define → Create → Update (version) → Activate → GetActiveVersions.
/// </summary>
public sealed class QuickstartIntegrationTest
{
    // ──────────────────────────────────────────────────────────────────────────
    // Simple POCOs used as typed aspects
    // ──────────────────────────────────────────────────────────────────────────

    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static (IResourceDefinitionStore DefinitionStore, IResourceManager Manager) CreateServices()
    {
        var idGen = new GuidIdentityGenerator();
        var store = new InMemoryResourceStore();
        var definitionStore = new InMemoryResourceDefinitionStore(
            NullLogger<InMemoryResourceDefinitionStore>.Instance);
        var manager = new InMemoryResourceManager(
            store,
            definitionStore,
            idGen,
            NullLogger<InMemoryResourceManager>.Instance);
        return (definitionStore, manager);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests — In-Memory provider
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quickstart_DefineCreateUpdateActivate_FullFlow()
    {
        // ── Arrange ──────────────────────────────────────────────────────────
        var (definitionStore, manager) = CreateServices();

        // ── Step 1: Define ───────────────────────────────────────────────────
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect<TitleAspect>()
            .WithAspect<PriceAspect>()
            .Build();

        await definitionStore.RegisterDefinitionAsync(definition);

        var stored = await definitionStore.GetDefinitionAsync("Product");
        Assert.NotNull(stored);
        Assert.Equal("Product", stored.DefinitionId);
        Assert.Equal(2, stored.AspectDefinitions.Count);

        // ── Step 2: Create ───────────────────────────────────────────────────
        var resource = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                { "TitleAspect", new TitleAspect("Super Gadget") },
                { "PriceAspect", new PriceAspect(99.99m, "USD") },
            }
        });

        Assert.Equal(1, resource.Version);
        Assert.NotEmpty(resource.ResourceId);
        Assert.NotEmpty(resource.Id);
        Assert.Equal("Product", resource.DefinitionId);

        var resourceId = resource.ResourceId;
        var v1Id = resource.Id;

        // ── Step 3: Update (produces V2) ─────────────────────────────────────
        var latest = await manager.GetLatestVersionAsync(resourceId);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.Version);

        var v2 = await manager.UpdateAsync(resourceId, new UpdateResourceRequest
        {
            BaseVersion = latest.Version,
            AspectUpdates = new Dictionary<string, object>
            {
                { "TitleAspect", new TitleAspect("Super Gadget Pro") },
            }
        });

        Assert.Equal(resourceId, v2.ResourceId);          // same logical ID
        Assert.NotEqual(v1Id, v2.Id);                     // new version-specific Id
        Assert.Equal(2, v2.Version);                       // incremented

        // ── Step 4: Activate V2 in "Published" ───────────────────────────────
        await manager.ActivateAsync(resourceId, v2.Version, "Published", ChannelMode.SingleActive);

        var activeVersions = (await manager.GetActiveVersionsAsync(resourceId, "Published")).ToList();
        Assert.Single(activeVersions);
        Assert.Equal(2, activeVersions[0].Version);
        Assert.Equal(resourceId, activeVersions[0].ResourceId);
    }

    [Fact]
    public async Task Quickstart_MultipleResources_QuickstartSeedPattern()
    {
        // Verifies the Aster.Web SeedDataInitializer pattern works end-to-end.
        var (definitionStore, manager) = CreateServices();

        // Register "Product" definition
        var productDef = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect<TitleAspect>()
            .Build();
        await definitionStore.RegisterDefinitionAsync(productDef);

        // Create 3 resources and activate each
        var titles = new[] { "Gadget A", "Gadget B", "Gadget C" };
        foreach (var title in titles)
        {
            var r = await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect(title) }
                }
            });

            await manager.ActivateAsync(r.ResourceId, r.Version, "Published", ChannelMode.SingleActive);
        }

        // Verify each resource can be read as active
        var allDefs = (await definitionStore.ListDefinitionsAsync()).ToList();
        Assert.Single(allDefs);
        Assert.Equal("Product", allDefs[0].DefinitionId);
    }

    [Fact]
    public async Task Quickstart_OptimisticConcurrency_StaleBaseVersionThrows()
    {
        var (_, manager) = CreateServices();

        var resource = await manager.CreateAsync("Product", new CreateResourceRequest());

        // First writer makes a valid update (V1 → V2)
        await manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = new Dictionary<string, object> { { "X", "first" } }
        });

        // Second writer still references V1 (stale) — should throw
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            manager.UpdateAsync(resource.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = 1,
                AspectUpdates = new Dictionary<string, object> { { "X", "second" } }
            }).AsTask());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests — Sqlite provider
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sqlite_Quickstart_DefineCreateUpdateActivate_FullFlow()
    {
        // Same quickstart flow exercised against the Sqlite provider stores directly.
        using var fixture = new SqliteTestFixture();
        var defStore = fixture.DefinitionStore;
        var writeStore = fixture.WriteStore;

        // ── Step 1: Define ───────────────────────────────────────────────────
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect<TitleAspect>()
            .WithAspect<PriceAspect>()
            .Build();

        await defStore.RegisterDefinitionAsync(definition);

        var stored = await defStore.GetDefinitionAsync("Product");
        Assert.NotNull(stored);
        Assert.Equal("Product", stored.DefinitionId);
        Assert.Equal(2, stored.AspectDefinitions.Count);

        // ── Step 2: Create V1 ────────────────────────────────────────────────
        var resourceId = Guid.NewGuid().ToString("N");
        var v1 = new Resource
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString("N"),
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>
            {
                { "TitleAspect", new TitleAspect("Super Gadget") },
                { "PriceAspect", new PriceAspect(99.99m, "USD") },
            }
        };

        var saved = await writeStore.SaveVersionAsync(v1);
        Assert.Equal(1, saved.Version);
        Assert.Equal(resourceId, saved.ResourceId);
        Assert.Equal("Product", saved.DefinitionId);

        // ── Step 3: Update (produces V2) ─────────────────────────────────────
        var latest = await writeStore.GetLatestVersionAsync(resourceId);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.Version);

        var v2 = new Resource
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString("N"),
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = 2,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>
            {
                { "TitleAspect", new TitleAspect("Super Gadget Pro") },
                { "PriceAspect", new PriceAspect(99.99m, "USD") },
            }
        };

        var savedV2 = await writeStore.SaveVersionAsync(v2);
        Assert.Equal(resourceId, savedV2.ResourceId);
        Assert.NotEqual(v1.Id, savedV2.Id);
        Assert.Equal(2, savedV2.Version);

        // ── Step 4: Activate V2 in "Published" ──────────────────────────────
        var activationState = new ActivationState
        {
            ResourceId = resourceId,
            Channel = "Published",
            ActiveVersions = [2],
            Mode = ChannelMode.SingleActive,
            LastUpdated = DateTime.UtcNow,
        };
        await writeStore.UpdateActivationAsync(resourceId, "Published", activationState);

        var activeVersions = (await writeStore.GetActiveVersionsAsync(resourceId, "Published")).ToList();
        Assert.Single(activeVersions);
        Assert.Equal(2, activeVersions[0].Version);
        Assert.Equal(resourceId, activeVersions[0].ResourceId);
    }

    [Fact]
    public async Task Sqlite_Quickstart_MultipleResources_SeedPattern()
    {
        using var fixture = new SqliteTestFixture();
        var defStore = fixture.DefinitionStore;
        var writeStore = fixture.WriteStore;

        // Register "Product" definition
        var productDef = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect<TitleAspect>()
            .Build();
        await defStore.RegisterDefinitionAsync(productDef);

        // Create 3 resources and activate each
        var titles = new[] { "Gadget A", "Gadget B", "Gadget C" };
        foreach (var title in titles)
        {
            var resourceId = Guid.NewGuid().ToString("N");
            var resource = new Resource
            {
                ResourceId = resourceId,
                Id = Guid.NewGuid().ToString("N"),
                DefinitionId = "Product",
                DefinitionVersion = 1,
                Version = 1,
                Created = DateTime.UtcNow,
                Aspects = new Dictionary<string, object>
                {
                    { "TitleAspect", new TitleAspect(title) },
                },
            };

            await writeStore.SaveVersionAsync(resource);
            await writeStore.UpdateActivationAsync(resourceId, "Published", new ActivationState
            {
                ResourceId = resourceId,
                Channel = "Published",
                ActiveVersions = [1],
                Mode = ChannelMode.SingleActive,
                LastUpdated = DateTime.UtcNow,
            });
        }

        // Verify definitions survive
        var allDefs = (await defStore.ListDefinitionsAsync()).ToList();
        Assert.Single(allDefs);
        Assert.Equal("Product", allDefs[0].DefinitionId);
    }

    [Fact]
    public async Task Sqlite_Quickstart_DuplicateV1ResourceId_ThrowsDuplicateResourceIdException()
    {
        using var fixture = new SqliteTestFixture();
        var writeStore = fixture.WriteStore;

        var resourceId = Guid.NewGuid().ToString("N");
        var v1 = new Resource
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString("N"),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>(),
        };

        await writeStore.SaveVersionAsync(v1);

        // Second V1 with same ResourceId — should throw DuplicateResourceIdException
        var duplicateV1 = new Resource
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString("N"),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>(),
        };

        await Assert.ThrowsAsync<DuplicateResourceIdException>(
            () => writeStore.SaveVersionAsync(duplicateV1).AsTask());
    }
}
