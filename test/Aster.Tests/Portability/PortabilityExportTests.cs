using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityExportTests : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager manager;
    private readonly IResourcePortabilityService portability;

    public PortabilityExportTests()
    {
        provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

        definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        manager = provider.GetRequiredService<IResourceManager>();
        portability = provider.GetRequiredService<IResourcePortabilityService>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ExportAsync_DefinitionsOnly_IncludesAllSelectedDefinitionVersions()
    {
        await RegisterDefinitionVersionsAsync("Product", count: 2);
        await RegisterDefinitionVersionsAsync("Order", count: 1);

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.DefinitionsOnly,
            DefinitionIds = ["Product"],
        });

        Assert.NotNull(result.Snapshot);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([1, 2], result.Snapshot.Definitions.Select(static definition => definition.Version).ToList());
        Assert.All(result.Snapshot.Definitions, static definition => Assert.Equal("Product", definition.DefinitionId));
        Assert.Empty(result.Snapshot.Resources);
        Assert.Empty(result.Snapshot.ActivationStates);
    }

    [Fact]
    public async Task ExportAsync_SelectedResourceLatestOnly_IncludesLatestVersionAndReferencedDefinition()
    {
        await RegisterDefinitionVersionsAsync("Product", count: 1);
        var (v1, v2) = await CreateTwoVersionResourceAsync();

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [v1.ResourceId],
            ResourceVersionScope = PortableResourceVersionScope.LatestOnly,
        });

        Assert.NotNull(result.Snapshot);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([v2.Version], result.Snapshot.Resources.Select(static resource => resource.Version).ToList());
        Assert.Single(result.Snapshot.Definitions);
        Assert.Equal(v2.DefinitionVersion, result.Snapshot.Definitions[0].Version);
    }

    [Fact]
    public async Task ExportAsync_SelectedResourceSpecificVersions_IncludesOnlyRequestedVersions()
    {
        await RegisterDefinitionVersionsAsync("Product", count: 1);
        var (v1, _) = await CreateTwoVersionResourceAsync();

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceVersionScope = PortableResourceVersionScope.SpecificVersions,
            SpecificResourceVersions =
            [
                new ResourceVersionReference
                {
                    ResourceId = v1.ResourceId,
                    Version = 1,
                },
            ],
        });

        Assert.NotNull(result.Snapshot);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([1], result.Snapshot.Resources.Select(static resource => resource.Version).ToList());
    }

    [Fact]
    public async Task ExportAsync_LatestOnly_SkipsActivationEntriesForExcludedVersions()
    {
        await RegisterDefinitionVersionsAsync("Product", count: 1);
        var (v1, v2) = await CreateTwoVersionResourceAsync();
        await ActivateVersionsAsync(v1.ResourceId, "Preview", [v1.Version, v2.Version]);

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [v1.ResourceId],
            ResourceVersionScope = PortableResourceVersionScope.LatestOnly,
        });

        Assert.NotNull(result.Snapshot);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.SkippedActivationEntry, diagnostic.Code);
        Assert.Equal(PortableDiagnosticSeverity.Warning, diagnostic.Severity);

        var skippedEntry = Assert.Single(result.SkippedActivationEntries);
        Assert.Equal(v1.ResourceId, skippedEntry.ResourceId);
        Assert.Equal(v1.Version, skippedEntry.Version);
        Assert.Equal("Preview", skippedEntry.Channel);

        var state = Assert.Single(result.Snapshot.ActivationStates);
        Assert.Equal([v2.Version], state.ActiveVersions);
    }

    [Fact]
    public async Task ExportAsync_DefinitionWithResources_IncludesMatchingResourcesOnly()
    {
        await RegisterDefinitionVersionsAsync("Product", count: 1);
        await RegisterDefinitionVersionsAsync("Order", count: 1);
        var product = await manager.CreateAsync("Product", new CreateResourceRequest());
        await manager.CreateAsync("Order", new CreateResourceRequest());

        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.DefinitionWithResources,
            DefinitionIds = ["Product"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.NotNull(result.Snapshot);
        Assert.Empty(result.Diagnostics);
        var resource = Assert.Single(result.Snapshot.Resources);
        Assert.Equal(product.ResourceId, resource.ResourceId);
        Assert.All(result.Snapshot.Definitions, static definition => Assert.Equal("Product", definition.DefinitionId));
    }

    private async Task RegisterDefinitionVersionsAsync(string definitionId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId(definitionId)
                .Build());
        }
    }

    private async Task<(Resource V1, Resource V2)> CreateTwoVersionResourceAsync()
    {
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v1.Version,
        });
        return (v1, v2);
    }

    private async Task ActivateVersionsAsync(string resourceId, string channel, IReadOnlyList<int> versions)
    {
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        await writer.UpdateActivationAsync(resourceId, channel, new ActivationState
        {
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = versions,
            LastUpdated = DateTime.UtcNow,
        });
    }
}
