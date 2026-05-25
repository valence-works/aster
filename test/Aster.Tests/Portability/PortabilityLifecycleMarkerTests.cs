using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityLifecycleMarkerTests
{
    [Fact]
    public async Task ExportAndImport_PreservesLifecycleMarkers()
    {
        await using var source = PolicyTestFixtures.CreateCoreProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(source);
        await PolicyTestFixtures.SaveResourceAsync(source, "product-1");
        await source.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        });
        var snapshot = (await source.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["product-1"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        })).Snapshot!;

        await using var target = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(snapshot);

        var marker = await target.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("product-1", Aster.Core.Models.Tenancy.TenantScope.Default);
        Assert.NotNull(marker);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, marker.State);
    }
}
