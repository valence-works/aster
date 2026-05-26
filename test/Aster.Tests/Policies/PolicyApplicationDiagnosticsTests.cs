using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyApplicationDiagnosticsTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_InvalidCandidateShapeFailsWithStableDiagnostic()
    {
        var result = await ApplyAsync(new ResourcePolicyApplicationCandidate
        {
            PolicyId = "",
            PolicyKind = ResourcePolicyKind.Archival,
            Outcome = ResourcePolicyOutcome.Archive,
            ResourceId = "product-1",
        });

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyApplicationCandidateInvalid);
    }

    [Fact]
    public async Task ApplyAsync_MissingResourceFailsWithTargetNotFoundDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);

        var result = await ApplyAsync(PolicyTestFixtures.ApplicationCandidate("missing"));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound);
    }

    [Fact]
    public async Task ApplyAsync_UnsupportedOutcomeAndPruningPreviewFailWithStableDiagnostics()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies:
            [
                PolicyTestFixtures.ArchivePolicy(),
                PolicyTestFixtures.PruningPolicy(),
            ]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");

        var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                PolicyTestFixtures.ApplicationCandidate(
                    "product-1",
                    outcome: ResourcePolicyOutcome.Retain),
                PolicyTestFixtures.ApplicationCandidate(
                    "product-1",
                    "keep-latest",
                    ResourcePolicyKind.VersionPruning,
                    ResourcePolicyOutcome.PrunePreview),
            ],
        });

        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyApplicationOutcomeUnsupported, result.Candidates[0].Diagnostics.Single().Code);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningPreviewOnly, result.Candidates[1].Diagnostics.Single().Code);
    }

    [Fact]
    public async Task ApplyAsync_StaleVersionFailsWithStableDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1", version: 2);

        var result = await ApplyAsync(PolicyTestFixtures.ApplicationCandidate("product-1", resourceVersion: 1));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyApplicationStaleCandidate);
    }

    [Fact]
    public async Task ApplyAsync_MissingPolicyFailsWithStableDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");

        var result = await ApplyAsync(PolicyTestFixtures.ApplicationCandidate("product-1", "missing-policy"));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMissing);
    }

    [Fact]
    public async Task ApplyAsync_MismatchedPolicyOutcomeFailsWithStableDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");

        var result = await ApplyAsync(PolicyTestFixtures.ApplicationCandidate(
            "product-1",
            policyKind: ResourcePolicyKind.SoftDelete,
            outcome: ResourcePolicyOutcome.SoftDelete));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyApplicationPolicyMismatch);
    }

    [Fact]
    public async Task ApplyAsync_ConflictingLifecycleOutcomesForSameResourceAllFailBeforeWrites()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies:
            [
                PolicyTestFixtures.ArchivePolicy(),
                PolicyTestFixtures.SoftDeletePolicy(),
            ]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");

        var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                PolicyTestFixtures.ApplicationCandidate("product-1"),
                PolicyTestFixtures.ApplicationCandidate(
                    "product-1",
                    "soft-delete-old",
                    ResourcePolicyKind.SoftDelete,
                    ResourcePolicyOutcome.SoftDelete),
            ],
        });

        Assert.Equal(2, result.FailedCount);
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyApplicationConflictingOutcome, candidate.Diagnostics.Single().Code));
        Assert.Null(await provider.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("product-1", Aster.Core.Models.Tenancy.TenantScope.Default));
    }

    [Fact]
    public async Task ApplyAsync_DuplicateMarkerConflictPropagatesFailureDiagnostics()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.SoftDeletePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates =
            [
                PolicyTestFixtures.ApplicationCandidate(
                    "product-1",
                    "soft-delete-old",
                    ResourcePolicyKind.SoftDelete,
                    ResourcePolicyOutcome.SoftDelete),
                PolicyTestFixtures.ApplicationCandidate(
                    "product-1",
                    "soft-delete-old",
                    ResourcePolicyKind.SoftDelete,
                    ResourcePolicyOutcome.SoftDelete),
            ],
        });

        Assert.Equal(2, result.FailedCount);
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, candidate.Diagnostics.Single().Code));
    }

    private async Task<ResourcePolicyApplicationResult> ApplyAsync(ResourcePolicyApplicationCandidate candidate) =>
        await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [candidate],
        });

    private static void AssertDiagnostic(ResourcePolicyApplicationResult result, string code)
    {
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(code, candidate.Diagnostics.Single().Code);
    }
}
