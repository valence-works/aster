using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPruningApplicationDiagnosticsTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_InvalidCandidateShapeFailsWithStableDiagnostic()
    {
        var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates =
                [
                    null!,
                    new ResourcePolicyPruningApplicationCandidate
                    {
                        PolicyId = "",
                        PolicyKind = ResourcePolicyKind.VersionPruning,
                        Outcome = ResourcePolicyOutcome.PrunePreview,
                        ResourceId = "versioned",
                        ResourceVersion = 1,
                    },
                ],
            });

        Assert.All(result.Candidates, candidate => Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Failed, candidate.Status));
        Assert.All(result.Candidates, candidate => Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid, candidate.Diagnostics.Single().Code));
    }

    [Fact]
    public async Task ApplyAsync_MissingResourceFailsWithTargetNotFoundDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.PruningPolicy()]);

        var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates = [PolicyTestFixtures.PruningCandidate("missing", resourceVersion: 1)],
            });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningTargetNotFound, candidate.Diagnostics.Single().Code);
    }

    [Fact]
    public async Task ApplyAsync_UnsupportedProviderFailsWithStableDiagnostic()
    {
        await using var customProvider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<IResourceVersionReader>(new StaticVersionReader())
            .BuildServiceProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(customProvider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 1)]);

        var result = await customProvider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates = [PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1)],
            });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningProviderUnsupported, candidate.Diagnostics.Single().Code);
    }

    [Fact]
    public async Task ApplyAsync_ProviderWriteFailureReturnsStableDiagnostic()
    {
        await using var failingProvider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<IResourceVersionPruningStore, FailingPruningStore>()
            .BuildServiceProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(failingProvider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 1)]);
        await PolicyTestFixtures.SaveResourceAsync(failingProvider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(failingProvider, "versioned", version: 2);

        var result = await failingProvider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates = [PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1)],
            });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningWriteFailed, candidate.Diagnostics.Single().Code);
    }

    [Fact]
    public async Task ApplyAsync_UnexpectedProviderWriteFailureReturnsStableDiagnostic()
    {
        await using var failingProvider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<IResourceVersionPruningStore, UnexpectedFailingPruningStore>()
            .BuildServiceProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(failingProvider, policies: [PolicyTestFixtures.PruningPolicy(retainedVersions: 1)]);
        await PolicyTestFixtures.SaveResourceAsync(failingProvider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(failingProvider, "versioned", version: 2);

        var result = await failingProvider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
            new ResourcePolicyPruningApplicationRequest
            {
                Candidates = [PolicyTestFixtures.PruningCandidate("versioned", resourceVersion: 1)],
            });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningWriteFailed, candidate.Diagnostics.Single().Code);
    }

    private sealed class StaticVersionReader : IResourceVersionReader
    {
        private static readonly Resource V1 = Version(1);
        private static readonly Resource V2 = Version(2);

        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Resource> resources = request.Scope switch
            {
                ResourceVersionScope.Latest => [V2],
                ResourceVersionScope.AllVersions => [V1, V2],
                ResourceVersionScope.Draft => [V1, V2],
                _ => [],
            };

            return ValueTask.FromResult(resources);
        }

        private static Resource Version(int version) =>
            new()
            {
                ResourceId = "versioned",
                Id = $"versioned-{version}",
                DefinitionId = "Product",
                DefinitionVersion = 1,
                Version = version,
                Created = DateTime.UtcNow.AddDays(-version),
            };
    }

    private sealed class FailingPruningStore : IResourceVersionPruningStore
    {
        public ValueTask<bool> PruneVersionAsync(
            string resourceId,
            int resourceVersion,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pruning write failed.");
    }

    private sealed class UnexpectedFailingPruningStore : IResourceVersionPruningStore
    {
        public ValueTask<bool> PruneVersionAsync(
            string resourceId,
            int resourceVersion,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            throw new DataMisalignedException("Unexpected pruning write failed.");
    }
}
