using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPruningApplicationResultTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_EmptyAndNullCandidatesReturnEmptyResults()
    {
        var pruning = provider.GetRequiredService<IResourcePolicyPruningApplicationService>();

        var empty = await pruning.ApplyAsync(new ResourcePolicyPruningApplicationRequest());
        var nullList = await pruning.ApplyAsync(new ResourcePolicyPruningApplicationRequest { Candidates = null! });

        Assert.Empty(empty.Candidates);
        Assert.Empty(nullList.Candidates);
    }

    [Fact]
    public async Task ApplyAsync_DuplicateCandidatesAreDeterministic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);

        var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates =
                [
                    PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1),
                    PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1),
                ],
            });

        Assert.Equal(1, result.PrunedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Collection(
            result.Candidates,
            first => Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, first.Status),
            second => Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Skipped, second.Status));
    }

    [Fact]
    public async Task ApplyAsync_AlreadyPrunedCandidateIsIdempotent()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);
        var pruning = provider.GetRequiredService<IResourcePolicyPruningApplicationService>();
        var request = new ResourcePolicyPruningApplicationRequest
        {
            Candidates = [PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1)],
        };

        await pruning.ApplyAsync(request);
        var result = await pruning.ApplyAsync(request);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned, candidate.Status);
        Assert.Equal(1, result.AlreadyPrunedCount);
    }
}
