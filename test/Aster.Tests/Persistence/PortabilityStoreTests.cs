using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class PortabilityStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task ExportAsync_UsesProviderPortabilityStoreRegisteredAfterAsterCore()
    {
        await using var provider = Database.CreateProvider();
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

    [SkippableFact]
    public async Task ExportAsync_SpecificVersions_ExportsOnlyRequestedVersion()
    {
        await using var provider = Database.CreateProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v1.Version,
        });

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceVersionScope = PortableResourceVersionScope.SpecificVersions,
            SpecificResourceVersions =
            [
                new ResourceVersionReference
                {
                    ResourceId = v1.ResourceId,
                    Version = v1.Version,
                },
            ],
        });

        Assert.NotNull(result.Snapshot);
        var resource = Assert.Single(result.Snapshot.Resources);
        Assert.Equal(v1.ResourceId, resource.ResourceId);
        Assert.Equal(v1.Version, resource.Version);
    }

    [SkippableFact]
    public async Task ExportAsync_SpecificVersions_DuplicateReferencesExportOneResource()
    {
        // The request's set may use any comparer, so equal references can both reach the store. The
        // composite-key VALUES join returns a row per key, so the store must collapse them (#171).
        await using var provider = Database.CreateProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var references = new HashSet<ResourceVersionReference>(ReferenceEqualityComparer.Instance)
        {
            new() { ResourceId = v1.ResourceId, Version = v1.Version },
            new() { ResourceId = v1.ResourceId, Version = v1.Version },
        };

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceVersionScope = PortableResourceVersionScope.SpecificVersions,
            SpecificResourceVersions = references,
        });

        Assert.NotNull(result.Snapshot);
        var resource = Assert.Single(result.Snapshot.Resources);
        Assert.Equal(v1.Version, resource.Version);
    }

    [SkippableFact]
    public async Task ImportAsync_PersistsSnapshotForLaterExport()
    {
        var snapshot = CreateSnapshot();
        PortableImportResult import;

        await using (var importProvider = Database.CreateProvider())
        {
            var portability = importProvider.GetRequiredService<IResourcePortabilityService>();
            import = await portability.ImportAsync(snapshot);
        }

        await using var exportProvider = Database.CreateProvider();
        var exportPortability = exportProvider.GetRequiredService<IResourcePortabilityService>();

        var export = await exportPortability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["sqlite-product-1"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.Equal(PortableImportStatus.Imported, import.Status);
        Assert.NotNull(export.Snapshot);
        Assert.Equal(snapshot.Definitions.Select(static definition => (definition.DefinitionId, definition.Id, definition.Version)), export.Snapshot.Definitions.Select(static definition => (definition.DefinitionId, definition.Id, definition.Version)));
        Assert.Equal(snapshot.Resources.Select(static resource => (resource.ResourceId, resource.Id, resource.DefinitionId, resource.DefinitionVersion, resource.Version)), export.Snapshot.Resources.Select(static resource => (resource.ResourceId, resource.Id, resource.DefinitionId, resource.DefinitionVersion, resource.Version)));
        Assert.Equal(snapshot.ActivationStates.Select(static state => (state.ResourceId, state.Channel, state.LastUpdated, Versions: string.Join(",", state.ActiveVersions))), export.Snapshot.ActivationStates.Select(static state => (state.ResourceId, state.Channel, state.LastUpdated, Versions: string.Join(",", state.ActiveVersions))));
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

    private static PortableSnapshot CreateSnapshot()
    {
        var created = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        return new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new()
                {
                    DefinitionId = "SqliteProduct",
                    Id = "sqlite-product-definition-v1",
                    Version = 1,
                },
            ],
            Resources =
            [
                new()
                {
                    ResourceId = "sqlite-product-1",
                    Id = "sqlite-product-1-v1",
                    DefinitionId = "SqliteProduct",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = created,
                },
            ],
            ActivationStates =
            [
                new()
                {
                    ResourceId = "sqlite-product-1",
                    Channel = "Published",
                    ActiveVersions = [1],
                    LastUpdated = created.AddMinutes(1),
                },
            ],
        };
    }
}

public sealed class SqliteJsonPortabilityStoreTests() : PortabilityStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbPortabilityStoreTests() : PortabilityStoreTests(new PostgresTestDatabase());
