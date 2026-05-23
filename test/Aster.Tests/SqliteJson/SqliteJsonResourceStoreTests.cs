using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Services;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonResourceStoreTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    private SqliteJsonResourceStore CreateStore() =>
        new(new SqliteJsonAsterOptions
        {
            ConnectionString = $"Data Source={databasePath}",
        });

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddAsterCore();
        services.AddAsterSqliteJson(options =>
        {
            options.ConnectionString = $"Data Source={databasePath}";
        });

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    [Fact]
    public async Task AddAsterCore_WithSqliteJsonProvider_PersistsThroughPublicServices()
    {
        // Arrange
        await using (var firstProvider = CreateServiceProvider())
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

        await using var secondProvider = CreateServiceProvider();
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

    [Fact]
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
        Assert.Equal(2, active[0].Version);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    private static DefaultResourceManager CreateManager(SqliteJsonResourceStore store) =>
        new(
            store,
            store,
            store,
            new GuidIdentityGenerator(),
            new ResourceLifecycleHookDispatcher(new ServiceCollection().BuildServiceProvider()),
            NullLogger<DefaultResourceManager>.Instance);

    private static string GetTitle(Resource resource)
    {
        var title = Assert.IsType<JsonElement>(resource.Aspects["Title"]);
        return title.GetProperty("title").GetString()!;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
