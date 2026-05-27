using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyApplicationResultTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_ReturnsOneResultPerInputAndDeterministicCountsForDuplicates()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var application = provider.GetRequiredService<IResourcePolicyApplicationService>();

        var result = await application.ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                PolicyTestFixtures.ApplicationCandidate("product-1"),
                PolicyTestFixtures.ApplicationCandidate("product-1"),
            ],
        });

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(1, result.AlreadySatisfiedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Collection(
            result.Candidates,
            first => Assert.Equal(ResourcePolicyApplicationCandidateStatus.Applied, first.Status),
            second => Assert.Equal(ResourcePolicyApplicationCandidateStatus.AlreadySatisfied, second.Status));
    }

    [Fact]
    public async Task ApplyAsync_RetryingSatisfiedCandidateReturnsAlreadySatisfied()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var application = provider.GetRequiredService<IResourcePolicyApplicationService>();
        var request = new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("product-1")],
        };

        await application.ApplyAsync(request);
        var retry = await application.ApplyAsync(request);

        var candidate = Assert.Single(retry.Candidates);
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.AlreadySatisfied, candidate.Status);
        Assert.Equal(1, retry.AlreadySatisfiedCount);
    }
}
