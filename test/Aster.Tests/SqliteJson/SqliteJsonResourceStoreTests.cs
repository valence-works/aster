using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson;

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

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
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
