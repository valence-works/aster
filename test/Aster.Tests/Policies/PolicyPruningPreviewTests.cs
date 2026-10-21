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

    [Fact]
    public async Task PreviewAsync_ActiveVersionOutsideRetainedCount_IsReportedNotProposed_SoApplyingThePreviewSucceeds()
    {
        // #187: the preview used to propose active versions that pruning application then refused.
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        for (var version = 1; version <= 4; version++)
            await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: version);
        await PolicyTestFixtures.ActivateAsync(provider, "versioned", 1);

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest());

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(2, candidate.ResourceVersion);
        var diagnostic = Assert.Single(preview.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningVersionProtectedActive, diagnostic.Code);
        Assert.Equal(("versioned", 1), (diagnostic.ResourceId, diagnostic.ResourceVersion));

        var applied = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(new ResourcePolicyPruningApplicationRequest
        {
            Candidates = preview.Candidates.Select(static outcome => new ResourcePolicyPruningApplicationCandidate
            {
                PolicyId = outcome.PolicyId,
                PolicyKind = outcome.PolicyKind,
                Outcome = outcome.Outcome,
                ResourceId = outcome.ResourceId,
                ResourceVersion = outcome.ResourceVersion,
            }).ToList(),
        });

        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, Assert.Single(applied.Candidates).Status);
        Assert.Equal([1, 3, 4], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static resource => resource.Version));
    }
}
