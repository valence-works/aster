using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyEvaluationPreviewTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PreviewAsync_ReturnsArchiveAndSoftDeleteCandidates()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies:
            [
                PolicyTestFixtures.ArchivePolicy("archive"),
                PolicyTestFixtures.SoftDeletePolicy("soft-delete"),
            ]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "old-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var evaluation = provider.GetRequiredService<IResourcePolicyEvaluationService>();

        var preview = await evaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Empty(preview.Diagnostics);
        Assert.Contains(preview.Candidates, static candidate => candidate.PolicyId == "archive" && candidate.Outcome == ResourcePolicyOutcome.Archive);
        Assert.Contains(preview.Candidates, static candidate => candidate.PolicyId == "soft-delete" && candidate.Outcome == ResourcePolicyOutcome.SoftDelete);
    }

    [Fact]
    public async Task PreviewAsync_AppliesActivationStateCriteria()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies:
            [
                PolicyTestFixtures.ArchivePolicy("archive-active") with
                {
                    Criteria = new ResourcePolicyCriteria
                    {
                        MinimumAge = TimeSpan.FromDays(30),
                        ActivationState = ResourcePolicyActivationState.Active,
                        ActivationChannel = "Published",
                        LifecycleState = ResourceLifecycleMarkerState.None,
                    },
                },
                PolicyTestFixtures.ArchivePolicy("archive-draft") with
                {
                    Criteria = new ResourcePolicyCriteria
                    {
                        MinimumAge = TimeSpan.FromDays(30),
                        ActivationState = ResourcePolicyActivationState.Draft,
                        LifecycleState = ResourceLifecycleMarkerState.None,
                    },
                },
            ]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "active-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await PolicyTestFixtures.SaveResourceAsync(provider, "draft-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await provider.GetRequiredService<IResourceManager>().ActivateAsync("active-product", 1, "Published");
        var evaluation = provider.GetRequiredService<IResourcePolicyEvaluationService>();

        var preview = await evaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Empty(preview.Diagnostics);
        Assert.Contains(preview.Candidates, static candidate => candidate.PolicyId == "archive-active" && candidate.ResourceId == "active-product");
        Assert.DoesNotContain(preview.Candidates, static candidate => candidate.PolicyId == "archive-active" && candidate.ResourceId == "draft-product");
        Assert.Contains(preview.Candidates, static candidate => candidate.PolicyId == "archive-draft" && candidate.ResourceId == "draft-product");
        Assert.DoesNotContain(preview.Candidates, static candidate => candidate.PolicyId == "archive-draft" && candidate.ResourceId == "active-product");
    }
}
