using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityImportTests : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager manager;
    private readonly IResourcePortabilityService portability;

    public PortabilityImportTests()
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
    public async Task ImportAsync_EmptyStore_PreservesDefinitionsResourcesAndActivationState()
    {
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Equal(1, result.Counts.Definitions);
        Assert.Equal(1, result.Counts.Resources);
        Assert.Equal(1, result.Counts.ResourceVersions);
        Assert.Equal(1, result.Counts.ActivationEntries);

        var exported = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["product-1"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.NotNull(exported.Snapshot);
        Assert.Equal(snapshot.Definitions.Select(static definition => (definition.DefinitionId, definition.Id, definition.Version)), exported.Snapshot.Definitions.Select(static definition => (definition.DefinitionId, definition.Id, definition.Version)));
        Assert.Equal(snapshot.Resources.Select(static resource => (resource.ResourceId, resource.Id, resource.DefinitionId, resource.DefinitionVersion, resource.Version)), exported.Snapshot.Resources.Select(static resource => (resource.ResourceId, resource.Id, resource.DefinitionId, resource.DefinitionVersion, resource.Version)));
        Assert.Equal(snapshot.ActivationStates.Select(static state => (state.ResourceId, state.Channel, state.LastUpdated, Versions: state.ActiveVersions.ToArray())), exported.Snapshot.ActivationStates.Select(static state => (state.ResourceId, state.Channel, state.LastUpdated, Versions: state.ActiveVersions.ToArray())));
    }

    [Fact]
    public async Task ImportAsync_IdenticalContentAlreadyExists_ReturnsNoOp()
    {
        var snapshot = CreateSnapshot();
        await portability.ImportAsync(snapshot);

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.NoOp, result.Status);
        Assert.Equal(0, result.Counts.Definitions);
        Assert.Equal(0, result.Counts.ResourceVersions);
        Assert.Equal(0, result.Counts.ActivationEntries);
        Assert.Equal(3, result.Counts.ReusedIdenticalItems);
        Assert.All(result.IdentityMap, static mapping => Assert.Equal(PortableIdentityMappingReason.ReusedIdentical, mapping.Reason));
    }

    [Fact]
    public async Task ImportAsync_StrictDivergentDefinitionCollision_FailsWithoutWritingResources()
    {
        await definitionStore.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build());
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.DivergentIdentityCollision, diagnostic.Code);
        Assert.Null(await manager.GetLatestVersionAsync("product-1"));
    }

    private static PortableSnapshot CreateSnapshot()
    {
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        return new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "Product",
                    Id = "product-definition-v1",
                    Version = 1,
                },
            ],
            Resources =
            [
                new Resource
                {
                    ResourceId = "product-1",
                    Id = "product-1-v1",
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = created,
                },
            ],
            ActivationStates =
            [
                new ActivationState
                {
                    ResourceId = "product-1",
                    Channel = "Published",
                    ActiveVersions = [1],
                    LastUpdated = created.AddMinutes(1),
                },
            ],
        };
    }
}
