using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPruningApplicationServiceTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_PrunesOnlySelectedPreviewCandidates()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "selected", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "selected", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "selected", version: 3);
        await PolicyTestFixtures.SaveResourceAsync(provider, "unselected", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "unselected", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "unselected", version: 3);

        var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates = [PolicyTestFixtures.PruningCandidate("selected", resourceVersion: 1)],
            });

        Assert.Equal(1, result.PrunedCount);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, Assert.Single(result.Candidates).Status);
        Assert.Equal([2, 3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "selected")).Select(static version => version.Version).ToList());
        Assert.Equal([1, 2, 3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "unselected")).Select(static version => version.Version).ToList());
    }
}
