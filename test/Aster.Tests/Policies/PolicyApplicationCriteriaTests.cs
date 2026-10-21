using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

/// <summary>
/// A submitted candidate is a claim, not proof: application re-checks the policy's criteria against the resource's
/// latest version, as the preview does, so a declared policy can't mark a resource it doesn't select.
/// </summary>
public sealed class PolicyApplicationCriteriaTests : IDisposable
{
    private static readonly DateTimeOffset AppliedAt = new(2026, 5, 27, 12, 30, 0, TimeSpan.Zero);

    private static readonly ResourcePolicyDeclaration ArchiveDrafts = PolicyTestFixtures.ArchivePolicy("archive-drafts") with
    {
        Criteria = new ResourcePolicyCriteria
        {
            ActivationState = ResourcePolicyActivationState.Draft,
            LifecycleState = ResourceLifecycleMarkerState.None,
        },
    };

    private static readonly ResourcePolicyDeclaration ArchivePublished = PolicyTestFixtures.ArchivePolicy("archive-published") with
    {
        Criteria = new ResourcePolicyCriteria
        {
            ActivationState = ResourcePolicyActivationState.Active,
            ActivationChannel = "Published",
        },
    };

    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_DraftPolicy_RejectsAnActiveResourceAndArchivesADraft()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [ArchiveDrafts]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "live");
        await PolicyTestFixtures.ActivateAsync(provider, "live", 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "draft");

        var result = await ApplyAsync(Candidate("live", "archive-drafts"), Candidate("draft", "archive-drafts"));

        AssertMismatch(result.Candidates[0], "criteria/activationState");
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.Applied, result.Candidates[1].Status);
        Assert.Null(await MarkerAsync("live"));
        Assert.Equal(ResourceLifecycleMarkerState.Archived, (await MarkerAsync("draft"))!.State);
    }

    [Fact]
    public async Task ApplyAsync_ActivePolicy_RejectsADraft()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [ArchivePublished]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "draft");

        var result = await ApplyAsync(Candidate("draft", "archive-published"));

        AssertMismatch(Assert.Single(result.Candidates), "criteria/activationState");
        Assert.Null(await MarkerAsync("draft"));
    }

    [Fact]
    public async Task ApplyAsync_MinimumAge_RejectsAResourceTooYoungAtAppliedAt()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "recent", created: AppliedAt.UtcDateTime.AddDays(-29));

        var result = await ApplyAsync(Candidate("recent", "archive-old"));

        AssertMismatch(Assert.Single(result.Candidates), "criteria/minimumAge");
        Assert.Null(await MarkerAsync("recent"));
    }

    [Fact]
    public async Task ApplyAsync_LifecycleState_RejectsAResourceInAnotherStateButKeepsRetriesSatisfied()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "soft-deleted");
        await PolicyTestFixtures.SaveResourceAsync(provider, "archived");
        var markers = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        await markers.ApplyAsync(new ResourceLifecycleMarkerRequest { ResourceId = "soft-deleted", State = ResourceLifecycleMarkerState.SoftDeleted, MarkedAt = AppliedAt });
        await markers.ApplyAsync(new ResourceLifecycleMarkerRequest { ResourceId = "archived", State = ResourceLifecycleMarkerState.Archived, MarkedAt = AppliedAt });

        var result = await ApplyAsync(Candidate("soft-deleted", "archive-old"), Candidate("archived", "archive-old"));

        AssertMismatch(result.Candidates[0], "criteria/lifecycleState");
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.AlreadySatisfied, result.Candidates[1].Status);
    }

    [Fact]
    public async Task ApplyAsync_RetryAfterTheCriteriaChanged_IsStillAlreadySatisfied()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [ArchiveDrafts]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "draft");
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.Applied, (await ApplyAsync(Candidate("draft", "archive-drafts"))).Candidates[0].Status);

        // Activated after it was archived: it no longer matches a drafts policy, but it is already archived.
        await PolicyTestFixtures.ActivateAsync(provider, "draft", 1);
        var retry = await ApplyAsync(Candidate("draft", "archive-drafts"));

        Assert.Equal(ResourcePolicyApplicationCandidateStatus.AlreadySatisfied, Assert.Single(retry.Candidates).Status);
    }

    private async Task<ResourcePolicyApplicationResult> ApplyAsync(params ResourcePolicyApplicationCandidate[] candidates) =>
        await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = AppliedAt,
            Candidates = [.. candidates],
        });

    private async Task<ResourceLifecycleMarker?> MarkerAsync(string resourceId) =>
        await provider.GetRequiredService<IResourceLifecycleMarkerStore>().GetMarkerAsync(resourceId, TenantScope.Default);

    private static ResourcePolicyApplicationCandidate Candidate(string resourceId, string policyId) =>
        PolicyTestFixtures.ApplicationCandidate(resourceId, policyId);

    private static void AssertMismatch(ResourcePolicyApplicationCandidateResult candidate, string path)
    {
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.Failed, candidate.Status);
        var diagnostic = Assert.Single(candidate.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMismatch, diagnostic.Code);
        Assert.Equal(path, diagnostic.Path);
    }
}
