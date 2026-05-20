using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonPortabilityStoreTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-portability-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    [Fact]
    public async Task ExportAsync_UsesSqlitePortabilityStoreRegisteredAfterAsterCore()
    {
        await using var provider = CreateServiceProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v1.Version,
        });
        await UpdateActivationAsync(provider, v1.ResourceId, [v1.Version, v2.Version]);

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [v1.ResourceId],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.NotNull(result.Snapshot);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([1, 2], result.Snapshot.Resources.Select(static resource => resource.Version).ToList());
        Assert.Single(result.Snapshot.Definitions);
        var activationState = Assert.Single(result.Snapshot.ActivationStates);
        Assert.Equal([1, 2], activationState.ActiveVersions);
    }

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

    private static async Task UpdateActivationAsync(
        IServiceProvider provider,
        string resourceId,
        IReadOnlyList<int> versions)
    {
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        await writer.UpdateActivationAsync(resourceId, "Published", new ActivationState
        {
            ResourceId = resourceId,
            Channel = "Published",
            ActiveVersions = versions,
            LastUpdated = DateTime.UtcNow,
        });
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
