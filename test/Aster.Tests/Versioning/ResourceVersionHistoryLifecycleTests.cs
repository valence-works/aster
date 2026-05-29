using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Versioning;

public sealed class ResourceVersionHistoryLifecycleTests : IDisposable
{
    private readonly ServiceProvider provider = ResourceVersionHistoryTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task GetHistoryAsync_IncludesCurrentLifecycleMarkerState()
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "lifecycle", versionCount: 3);
        await ResourceVersionHistoryTestFixtures.MarkAsync(provider, "lifecycle", ResourceLifecycleMarkerState.Archived);

        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "lifecycle" });

        Assert.All(result.Versions, version => Assert.Equal(ResourceLifecycleMarkerState.Archived, version.LifecycleState));
    }

    [Fact]
    public async Task GetHistoryAsync_MapsMaintenanceDispositionConservatively()
    {
        await ResourceVersionHistoryTestFixtures.SaveVersionsAsync(provider, "maintenance", versionCount: 3);
        await ResourceVersionHistoryTestFixtures.ActivateAsync(provider, "maintenance", "Published", [2]);

        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoryAsync(
            new ResourceVersionHistoryRequest { ResourceId = "maintenance" });

        Assert.Equal(ResourceVersionMaintenanceDisposition.PossibleCandidate, result.Versions[0].MaintenanceDisposition);
        Assert.False(result.Versions[0].IsProtectedFromPruning);

        Assert.Equal(ResourceVersionMaintenanceDisposition.Protected, result.Versions[1].MaintenanceDisposition);
        Assert.True(result.Versions[1].IsProtectedFromPruning);

        Assert.Equal(ResourceVersionMaintenanceDisposition.Protected, result.Versions[2].MaintenanceDisposition);
        Assert.True(result.Versions[2].IsProtectedFromPruning);
    }

}
