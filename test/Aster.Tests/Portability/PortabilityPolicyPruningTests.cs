using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Portability;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityPolicyPruningTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ExportAsync_AfterPruningOmitsRemovedVersionWithoutFormatChange()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);
        await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates = [PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1)],
            });

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["versioned"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(PortableSnapshot.CurrentFormatVersion, result.Snapshot.FormatVersion);
        Assert.Equal([2, 3], result.Snapshot.Resources.Select(static resource => resource.Version).ToList());
    }
}
