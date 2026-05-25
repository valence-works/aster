using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPruningPreviewTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PreviewAsync_ReturnsPruningCandidatesOutsideRetainedVersionCount()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);
        var evaluation = provider.GetRequiredService<IResourcePolicyEvaluationService>();

        var preview = await evaluation.PreviewAsync(new ResourcePolicyEvaluationRequest());

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(ResourcePolicyOutcome.PrunePreview, candidate.Outcome);
        Assert.Equal(1, candidate.ResourceVersion);
        Assert.Empty(preview.Diagnostics);
    }
}
