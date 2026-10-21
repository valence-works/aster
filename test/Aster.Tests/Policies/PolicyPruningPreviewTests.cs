using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
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
            Candidates = PolicyTestFixtures.PruningCandidates(preview),
        });

        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, Assert.Single(applied.Candidates).Status);
        Assert.Equal([1, 3, 4], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static resource => resource.Version));
    }

    [Fact]
    public async Task PreviewAsync_LifecycleStateCriterion_ProposesOnlyMatchingResources_SoApplyingThePreviewPrunesEveryCandidate()
    {
        // #206: the preview used to ignore the lifecycle state, proposing versions application then refused.
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies: [PolicyTestFixtures.PruningPolicy("prune-archived", retainedVersions: 1, lifecycleState: ResourceLifecycleMarkerState.Archived)]);
        foreach (var resourceId in new[] { "archived", "unmarked" })
        {
            for (var version = 1; version <= 3; version++)
                await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, version: version);
        }

        await PolicyTestFixtures.ArchiveAsync(provider, "archived");

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest());

        Assert.Equal(
            [("archived", 1), ("archived", 2)],
            preview.Candidates.Select(static candidate => (candidate.ResourceId, candidate.ResourceVersion)).Order());
        Assert.Empty(preview.Diagnostics);

        var applied = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(new ResourcePolicyPruningApplicationRequest
        {
            Candidates = PolicyTestFixtures.PruningCandidates(preview),
        });

        Assert.All(applied.Candidates, static result => Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, result.Status));
        Assert.Equal(2, applied.PrunedCount);
        Assert.Equal([3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "archived")).Select(static resource => resource.Version));
        Assert.Equal([1, 2, 3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "unmarked")).Select(static resource => resource.Version));
    }

    [Fact]
    public async Task PreviewAsync_MinimumAgeCriterion_ProposesOnlyOldEnoughVersions_SoApplyingThePreviewPrunesEveryCandidate()
    {
        // #206: the preview used to ignore the minimum age for pruning, proposing versions application then refused.
        // Retention still keeps the newest version whatever its age; v2 is outside it but too young.
        var evaluatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies: [PolicyTestFixtures.PruningPolicy("prune-old", retainedVersions: 1, minimumAge: TimeSpan.FromDays(30))]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2, created: evaluatedAt.UtcDateTime.AddDays(-1));
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3, created: evaluatedAt.UtcDateTime.AddHours(-1));

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = evaluatedAt,
        });

        Assert.Equal(1, Assert.Single(preview.Candidates).ResourceVersion);
        Assert.Empty(preview.Diagnostics);

        var applied = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(new ResourcePolicyPruningApplicationRequest
        {
            AppliedAt = evaluatedAt,
            Candidates = PolicyTestFixtures.PruningCandidates(preview),
        });

        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, Assert.Single(applied.Candidates).Status);
        Assert.Equal([2, 3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static resource => resource.Version));
    }
}
