using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Services;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Persistence;

/// <summary>
/// Store-level persistence tests, run against each persistent provider.
/// </summary>
public abstract class ResourceStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    /// <summary>
    /// The provider's store over <see cref="Database"/>, from a new provider as a fresh process would open it.
    /// </summary>
    private protected Store CreateStore() => new(Database.CreateProvider());

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task AddAsterCore_WithPersistentProvider_PersistsThroughPublicServices()
    {
        // Arrange
        await using (var firstProvider = Database.CreateProvider())
        {
            var definitionStore = firstProvider.GetRequiredService<IResourceDefinitionStore>();
            var manager = firstProvider.GetRequiredService<IResourceManager>();

            await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId("Product")
                .Build());

            var resource = await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["Title"] = new { Title = "Public Services" },
                },
            });
            await manager.ActivateAsync(resource.ResourceId, resource.Version, "Published");
        }

        await using var secondProvider = Database.CreateProvider();
        var query = secondProvider.GetRequiredService<IResourceQueryService>();

        // Act
        var results = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
            DefinitionId = "Product",
        })).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("Public Services", GetTitle(results[0]));
    }

    [SkippableFact]
    public async Task DefaultResourceManager_PersistsLifecycleAcrossStoreInstances()
    {
        // Arrange
        var firstStore = CreateStore();
        var manager = CreateManager(firstStore);

        await firstStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());

        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                ["Title"] = new { Title = "Draft" },
            },
        });
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = new Dictionary<string, object>
            {
                ["Title"] = new { Title = "Published" },
            },
        });
        await manager.ActivateAsync(v1.ResourceId, v2.Version, "Published");
        await manager.ActivateAsync(v1.ResourceId, v1.Version, "Published");

        var secondStore = CreateStore();
        var secondManager = CreateManager(secondStore);

        // Act
        var latest = await secondManager.GetLatestVersionAsync(v1.ResourceId);
        var active = (await secondManager.GetActiveVersionsAsync(v1.ResourceId, "Published")).ToList();

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
        Assert.Equal("Published", GetTitle(latest));
        Assert.Single(active);
        Assert.Equal(1, active[0].Version);
        Assert.Equal("Draft", GetTitle(active[0]));
    }

    [SkippableFact]
    public async Task Definitions_PersistAcrossStoreInstances()
    {
        // Arrange
        var v1 = new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();
        var v2 = new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();

        var first = CreateStore();
        await first.RegisterDefinitionAsync(v1);
        await first.RegisterDefinitionAsync(v2);

        var second = CreateStore();

        // Act
        var latest = await second.GetDefinitionAsync("Product");
        var firstVersion = await second.GetDefinitionVersionAsync("Product", 1);
        var allDefinitions = (await second.ListDefinitionsAsync()).ToList();

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
        Assert.NotNull(firstVersion);
        Assert.Equal(1, firstVersion.Version);
        Assert.Single(allDefinitions);
    }

    [SkippableFact]
    public async Task ResourceVersions_PersistAcrossStoreInstances()
    {
        // Arrange
        var v1 = CreateResource(resourceId: "product-1", version: 1, title: "Alpha");
        var v2 = CreateResource(resourceId: "product-1", version: 2, title: "Beta");

        var first = CreateStore();
        await first.SaveVersionAsync(v1);
        await first.SaveVersionAsync(v2);

        var second = CreateStore();

        // Act
        var latest = (await second.ReadVersionsAsync(new ResourceVersionReadRequest())).ToList();
        var allVersions = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions
        })).ToList();

        // Assert
        Assert.Single(latest);
        Assert.Equal(2, latest[0].Version);
        Assert.Equal("Beta", GetTitle(latest[0]));
        Assert.Equal([1, 2], allVersions.Select(r => r.Version).ToList());
    }

    [SkippableFact]
    public async Task ReadVersionsAsync_ResourceIdsBoundsLatestAndAllVersionReads()
    {
        var first = CreateStore();
        await first.SaveVersionAsync(CreateResource(resourceId: "product-1", version: 1, title: "Alpha"));
        await first.SaveVersionAsync(CreateResource(resourceId: "product-1", version: 2, title: "Beta"));
        await first.SaveVersionAsync(CreateResource(resourceId: "product-2", version: 1, title: "Gamma"));

        var second = CreateStore();

        var latest = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = ["product-1"],
        })).ToList();
        var allVersions = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
            ResourceIds = ["product-1"],
        })).ToList();

        var latestVersion = Assert.Single(latest);
        Assert.Equal("product-1", latestVersion.ResourceId);
        Assert.Equal(2, latestVersion.Version);
        Assert.Equal([1, 2], allVersions.Select(static resource => resource.Version).ToList());
        Assert.All(allVersions, static resource => Assert.Equal("product-1", resource.ResourceId));
    }

    [SkippableFact]
    public async Task ReadVersionsAsync_ResourceIdsNormalizationPreservesBoundedIntent()
    {
        var first = CreateStore();
        await first.SaveVersionAsync(CreateResource(resourceId: "product-1", version: 1, title: "Alpha"));
        await first.SaveVersionAsync(CreateResource(resourceId: "product-2", version: 1, title: "Beta"));

        var second = CreateStore();

        var nullBound = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = null,
        })).ToList();
        var emptyBound = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = [],
        })).ToList();
        var invalidOnly = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = ["", " "],
        })).ToList();
        var nonOrdinalComparer = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PRODUCT-1" },
        })).ToList();

        Assert.Equal(2, nullBound.Count);
        Assert.Equal(2, emptyBound.Count);
        Assert.Empty(invalidOnly);
        Assert.Empty(nonOrdinalComparer);
    }

    [SkippableFact]
    public async Task ActivationState_PersistsAndScopesReads()
    {
        // Arrange
        var active = CreateResource(resourceId: "product-1", version: 1, title: "Active");
        var draft = CreateResource(resourceId: "product-2", version: 1, title: "Draft");

        var first = CreateStore();
        await first.SaveVersionAsync(active);
        await first.SaveVersionAsync(draft);
        await first.UpdateActivationAsync(active.ResourceId, "Published", new ActivationState
        {
            ResourceId = active.ResourceId,
            Channel = "Published",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });

        var second = CreateStore();

        // Act
        var activeResults = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        })).ToList();
        var draftResults = (await second.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Draft,
        })).ToList();

        // Assert
        Assert.Single(activeResults);
        Assert.Equal("product-1", activeResults[0].ResourceId);
        Assert.Single(draftResults);
        Assert.Equal("product-2", draftResults[0].ResourceId);
    }

    [SkippableFact]
    public async Task SaveVersionAsync_VersionAlreadyStored_ThrowsConcurrencyAndKeepsOneRow()
    {
        // A writer that lost the race between the manager's base-version check and the insert.
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("race", 1, "first"));
        await store.SaveVersionAsync(CreateResource("race", 2, "winner"));

        await Assert.ThrowsAsync<ConcurrencyException>(async () => await store.SaveVersionAsync(CreateResource("race", 2, "loser")));
        await Assert.ThrowsAsync<DuplicateResourceIdException>(async () => await store.SaveVersionAsync(CreateResource("race", 1, "again")));

        var versions = (await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
            ResourceIds = ["race"],
        })).ToList();
        Assert.Equal([1, 2], versions.Select(static resource => resource.Version));
        Assert.Equal("winner", GetTitle(versions[1]));
    }

    [SkippableFact]
    public async Task UpdateActivationAsync_VersionThatDoesNotExist_ThrowsAndWritesNothing()
    {
        // #180: the existence check runs in the same serialized write as the activation, so an activation can't name
        // a version a concurrent prune deleted.
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("activated", 1, "only"));

        await Assert.ThrowsAsync<VersionNotFoundException>(async () => await store.UpdateActivationAsync("activated", "Published", new ActivationState
        {
            ResourceId = "activated",
            Channel = "Published",
            ActiveVersions = [1, 2],
            LastUpdated = DateTime.UtcNow,
        }));

        Assert.Empty(await store.ReadActivationStatesAsync(["activated"], TenantScope.Default));
    }

    [SkippableFact]
    public async Task LatestByIds_ReturnsEachResourcesLatestVersion_AndSkipsMissingIds()
    {
        var store = CreateStore();
        await SaveAsync(store, "a", 1);
        await SaveAsync(store, "a", 2);
        await SaveAsync(store, "b", 1);
        await SaveAsync(store, "c", 1);
        await SaveAsync(store, "a", 1, TenantScope.FromTenantId("other"));
        await SaveAsync(store, "a", 5, TenantScope.FromTenantId("other"));

        var latest = await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Latest,
            ResourceIds = ["b", "missing", "a"],
        });

        Assert.Equal(new[] { ("a", 2), ("b", 1) }, latest.Select(static r => (r.ResourceId, r.Version)));
    }

    private static Task<Resource> SaveAsync(Store store, string resourceId, int version, TenantScope? tenant = null) =>
        store.SaveVersionAsync(new Resource
        {
            ResourceId = resourceId,
            Id = $"{resourceId}-{version}-{tenant?.TenantId}",
            DefinitionId = "Article",
            Version = version,
            Created = DateTime.UtcNow,
            TenantScope = tenant ?? TenantScope.Default,
        }).AsTask();

    private static Resource CreateResource(string resourceId, int version, string title) =>
        new()
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = version,
            Created = DateTime.UtcNow.AddMinutes(version),
            Aspects = new Dictionary<string, object>
            {
                ["Title"] = new { Title = title },
            },
        };

    private static DefaultResourceManager CreateManager(Store store) =>
        new(
            store,
            store,
            store,
            new GuidIdentityGenerator(),
            new Lifecycle.NoopResourceLifecycleHookDispatcher(),
            NullLogger<DefaultResourceManager>.Instance);

    private static string GetTitle(Resource resource)
    {
        var title = Assert.IsType<JsonElement>(resource.Aspects["Title"]);
        return title.GetProperty("title").GetString()!;
    }

    /// <summary>
    /// The definition, version and activation services of one provider instance as one object, so the tests
    /// read like tests of a single store.
    /// </summary>
    private protected sealed class Store(ServiceProvider provider) :
        IResourceDefinitionStore,
        IResourceVersionReader,
        IResourceVersionWriter,
        IResourceActivationStateReader
    {
        private IResourceDefinitionStore Definitions => provider.GetRequiredService<IResourceDefinitionStore>();
        private IResourceVersionReader Reader => provider.GetRequiredService<IResourceVersionReader>();
        private IResourceVersionWriter Writer => provider.GetRequiredService<IResourceVersionWriter>();
        private IResourceActivationStateReader Activations => provider.GetRequiredService<IResourceActivationStateReader>();

        public ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Definitions.GetDefinitionAsync(definitionId, cancellationToken);

        public ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, TenantScope tenantScope, CancellationToken cancellationToken) =>
            Definitions.GetDefinitionAsync(definitionId, tenantScope, cancellationToken);

        public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default) =>
            Definitions.GetDefinitionVersionAsync(definitionId, version, cancellationToken);

        public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, TenantScope tenantScope, CancellationToken cancellationToken) =>
            Definitions.GetDefinitionVersionAsync(definitionId, version, tenantScope, cancellationToken);

        public ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default) =>
            Definitions.RegisterDefinitionAsync(definition, cancellationToken);

        public ValueTask RegisterDefinitionAsync(ResourceDefinition definition, TenantScope tenantScope, CancellationToken cancellationToken) =>
            Definitions.RegisterDefinitionAsync(definition, tenantScope, cancellationToken);

        public ValueTask<ResourceDefinitionRegistrationResult> RegisterDefinitionIfLatestAsync(
            ResourceDefinition definition, int expectedLatestVersion, TenantScope tenantScope, CancellationToken cancellationToken) =>
            Definitions.RegisterDefinitionIfLatestAsync(definition, expectedLatestVersion, tenantScope, cancellationToken);

        public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            Definitions.ListDefinitionsAsync(cancellationToken);

        public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(TenantScope tenantScope, CancellationToken cancellationToken) =>
            Definitions.ListDefinitionsAsync(tenantScope, cancellationToken);

        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(ResourceVersionReadRequest request, CancellationToken cancellationToken = default) =>
            Reader.ReadVersionsAsync(request, cancellationToken);

        public ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default) =>
            Writer.SaveVersionAsync(resource, cancellationToken);

        public ValueTask<ActivationState> UpdateActivationAsync(string resourceId, string channel, ActivationState state, CancellationToken cancellationToken = default) =>
            Writer.UpdateActivationAsync(resourceId, channel, state, cancellationToken);

        public ValueTask<IReadOnlyList<ActivationState>> ReadActivationStatesAsync(IEnumerable<string> resourceIds, TenantScope tenantScope, CancellationToken cancellationToken = default) =>
            Activations.ReadActivationStatesAsync(resourceIds, tenantScope, cancellationToken);
    }
}

public sealed class SqliteJsonResourceStoreTests() : ResourceStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbResourceStoreTests() : ResourceStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonResourceStoreTests() : ResourceStoreTests(new SqlServerTestDatabase());
