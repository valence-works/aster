using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPruningApplicationSafetyTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_LatestVersionFailsClosed()
    {
        await SetupVersionedResourceAsync();

        var result = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 3));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyPruningVersionProtectedLatest);
        Assert.Equal([1, 2, 3], await ReadVersionNumbersAsync());
    }

    [Fact]
    public async Task ApplyAsync_ActiveVersionFailsClosed()
    {
        await SetupVersionedResourceAsync();
        await PolicyTestFixtures.ActivateAsync(provider, "versioned", version: 1);

        var result = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyPruningVersionProtectedActive);
        Assert.Equal([1, 2, 3], await ReadVersionNumbersAsync());
    }

    [Fact]
    public async Task ApplyAsync_PolicyMissingAndMismatchFailClosed()
    {
        await SetupVersionedResourceAsync();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: []);

        var missing = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1));

        AssertDiagnostic(missing, ResourcePolicyDiagnosticCodes.PolicyPruningPolicyMissing);

        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy("keep-latest")]);
        var mismatch = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1));

        AssertDiagnostic(mismatch, ResourcePolicyDiagnosticCodes.PolicyPruningPolicyMismatch);
        Assert.Equal([1, 2, 3], await ReadVersionNumbersAsync());
    }

    [Fact]
    public async Task ApplyAsync_LifecycleCriteriaMismatchFailsClosed()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies: [PolicyTestFixtures.PruningPolicy("keep-latest", retainedVersions: 2, lifecycleState: ResourceLifecycleMarkerState.None)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "versioned",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        var result = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyPruningPolicyMismatch);
        Assert.Equal([1, 2, 3], await ReadVersionNumbersAsync());
    }

    [Fact]
    public async Task ApplyAsync_RetainedVersionSafetyFailsClosed()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 3)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);

        var result = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyPruningUnsafe);
        Assert.Equal([1, 2, 3], await ReadVersionNumbersAsync());
    }

    [Fact]
    public async Task ApplyAsync_RetainedVersionSafetyUsesDraftMatchedVersions()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy("keep-latest", retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);
        await PolicyTestFixtures.ActivateAsync(provider, "versioned", version: 2);

        var result = await ApplyAsync(PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1));

        AssertDiagnostic(result, ResourcePolicyDiagnosticCodes.PolicyPruningUnsafe);
        Assert.Equal([1, 2, 3], await ReadVersionNumbersAsync());
    }

    private async Task SetupVersionedResourceAsync()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 2)]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 3);
    }

    private async Task<ResourcePolicyPruningApplicationResult> ApplyAsync(ResourcePolicyPruningApplicationCandidate candidate) =>
        await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(new ResourcePolicyPruningApplicationRequest
        {
            Candidates = [candidate],
        });

    private async Task<List<int>> ReadVersionNumbersAsync() =>
        (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList();

    private static void AssertDiagnostic(ResourcePolicyPruningApplicationResult result, string code)
    {
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(code, candidate.Diagnostics.Single().Code);
    }
}
