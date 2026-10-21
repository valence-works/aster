using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Policies;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class PolicyPruningApplicationStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task ApplyAsync_PrunesPersistedVersionWithinRequestedTenantOnly()
    {
        await using (var provider = Database.CreateProvider())
        {
            await RegisterTenantResourceAsync(provider, TenantScopeTestFixtures.TenantA);
            await RegisterTenantResourceAsync(provider, TenantScopeTestFixtures.TenantB);

            var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
                new ResourcePolicyPruningApplicationRequest
                {
                    TenantScope = TenantScopeTestFixtures.TenantA,
                    Candidates = [PolicyTestFixtures.PruningCandidate("shared", resourceVersion: 1)],
                });

            Assert.Equal(1, result.PrunedCount);
            Assert.Equal([2, 3], await ReadVersionsAsync(provider, TenantScopeTestFixtures.TenantA));
            Assert.Equal([1, 2, 3], await ReadVersionsAsync(provider, TenantScopeTestFixtures.TenantB));
            Assert.Equal([3], await ReadActiveVersionsAsync(provider, TenantScopeTestFixtures.TenantA));
            Assert.NotNull(await provider.GetRequiredService<IResourceLifecycleMarkerStore>().GetMarkerAsync("shared", TenantScopeTestFixtures.TenantA));
            Assert.NotNull(await provider.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None));
        }

        await using var secondProvider = Database.CreateProvider();

        Assert.Equal([2, 3], await ReadVersionsAsync(secondProvider, TenantScopeTestFixtures.TenantA));
        Assert.Equal([1, 2, 3], await ReadVersionsAsync(secondProvider, TenantScopeTestFixtures.TenantB));
    }

    [SkippableFact]
    public async Task PreviewThenApply_LifecycleStateCriterion_PrunesOnlyThePersistedArchivedResource()
    {
        // #206: the preview reads persisted markers and proposes only what application then prunes.
        await using var provider = Database.CreateProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies: [PolicyTestFixtures.PruningPolicy("prune-archived", retainedVersions: 1, lifecycleState: ResourceLifecycleMarkerState.Archived)]);
        foreach (var resourceId in new[] { "archived", "unmarked" })
        {
            foreach (var version in new[] { 1, 2, 3 })
                await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, version);
        }

        await PolicyTestFixtures.ArchiveAsync(provider, "archived");

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest());
        var applied = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(new ResourcePolicyPruningApplicationRequest
        {
            Candidates = PolicyTestFixtures.PruningCandidates(preview),
        });

        Assert.Equal(
            [("archived", 1), ("archived", 2)],
            preview.Candidates.Select(static candidate => (candidate.ResourceId, candidate.ResourceVersion)).Order());
        Assert.All(applied.Candidates, static result => Assert.Equal(ResourcePolicyPruningApplicationCandidateStatus.Pruned, result.Status));
        Assert.Equal([3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "archived")).Select(static version => version.Version));
        Assert.Equal([1, 2, 3], (await PolicyTestFixtures.ReadVersionsAsync(provider, "unmarked")).Select(static version => version.Version));
    }

    [SkippableFact]
    public async Task PruneVersionAsync_FailsClosedForPersistedLatestVersion()
    {
        await using var provider = Database.CreateProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        var pruningStore = provider.GetRequiredService<IResourceVersionPruningStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pruningStore.PruneVersionAsync("versioned", 2, TenantScope.Default));

        Assert.Equal([1, 2], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());
    }

    [SkippableFact]
    public async Task PruneVersionAsync_FailsClosedForPersistedActiveVersion()
    {
        await using var provider = Database.CreateProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.ActivateAsync(provider, "versioned", version: 1);
        var pruningStore = provider.GetRequiredService<IResourceVersionPruningStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pruningStore.PruneVersionAsync("versioned", 1, TenantScope.Default));

        Assert.Equal([1, 2], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());
    }

    [SkippableFact]
    public async Task PruneVersionAsync_WithAFloor_RefusesTheDeleteThatWouldBreakIt()
    {
        // v4 is latest and v3 active; each prune counts what would remain, active versions only when the floor says.
        await using var provider = Database.CreateProvider();
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        foreach (var version in new[] { 1, 2, 3, 4 })
            await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version);
        await PolicyTestFixtures.ActivateAsync(provider, "versioned", version: 3);
        var pruningStore = provider.GetRequiredService<IResourceVersionPruningStore>();

        Assert.True(await pruningStore.PruneVersionAsync("versioned", 1, TenantScope.Default, new ResourceVersionPruneFloor(2, InactiveVersionsOnly: true)));
        await Assert.ThrowsAsync<ResourceVersionPruneFloorException>(async () =>
            await pruningStore.PruneVersionAsync("versioned", 2, TenantScope.Default, new ResourceVersionPruneFloor(2, InactiveVersionsOnly: true)));
        Assert.Equal([2, 3, 4], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());

        Assert.True(await pruningStore.PruneVersionAsync("versioned", 2, TenantScope.Default, new ResourceVersionPruneFloor(2, InactiveVersionsOnly: false)));
        Assert.Equal([3, 4], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());
    }

    [SkippableFact]
    public async Task ApplyAsync_TwoPrunesUnderAFloorThatAllowsOne_NeverBothSucceed()
    {
        // #199: versions 1-3, none active, and a policy that keeps 2. Each request alone may prune, and each reads
        // three versions before writing, so only the store's floor check stops the second delete.
        const int Resources = 20;
        await using (var setup = Database.CreateProvider())
        {
            await PolicyTestFixtures.RegisterProductDefinitionAsync(setup, policies: [PolicyTestFixtures.PruningPolicy("keep-two", retainedVersions: 2)]);
            for (var index = 0; index < Resources; index++)
            {
                foreach (var version in new[] { 1, 2, 3 })
                    await PolicyTestFixtures.SaveResourceAsync(setup, $"raced-{index}", version);
            }
        }

        // Separate providers, as separate processes would have.
        await using var first = Database.CreateProvider();
        await using var second = Database.CreateProvider();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var races = Enumerable.Range(0, Resources).Select(index => Task.WhenAll(
            PruneAsync(first, $"raced-{index}", 1),
            PruneAsync(second, $"raced-{index}", 2))).ToList();
        start.SetResult();

        foreach (var (outcomes, index) in (await Task.WhenAll(races)).Select((outcomes, index) => (outcomes, index)))
        {
            var pruned = Assert.Single(outcomes, static outcome => outcome.Status == ResourcePolicyPruningApplicationCandidateStatus.Pruned);
            var refused = Assert.Single(outcomes, static outcome => outcome.Status == ResourcePolicyPruningApplicationCandidateStatus.Failed);
            Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyPruningUnsafe, Assert.Single(refused.Diagnostics).Code);
            Assert.Equal(
                new[] { 1, 2, 3 }.Where(version => version != pruned.ResourceVersion),
                (await PolicyTestFixtures.ReadVersionsAsync(first, $"raced-{index}")).Select(static version => version.Version));
        }

        async Task<ResourcePolicyPruningApplicationCandidateResult> PruneAsync(IServiceProvider provider, string resourceId, int version)
        {
            await start.Task;
            var result = await provider.GetRequiredService<IResourcePolicyPruningApplicationService>().ApplyAsync(
                new ResourcePolicyPruningApplicationRequest
                {
                    Candidates = [PolicyTestFixtures.PruningCandidate(resourceId, version, policyId: "keep-two")],
                });
            return Assert.Single(result.Candidates);
        }
    }

    private static async Task RegisterTenantResourceAsync(IServiceProvider provider, TenantScope tenant)
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            tenant,
            PolicyTestFixtures.PruningPolicy("keep-latest", retainedVersions: 1, lifecycleState: ResourceLifecycleMarkerState.Archived));
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", version: 1, tenantScope: tenant);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", version: 2, tenantScope: tenant);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", version: 3, tenantScope: tenant);
        await PolicyTestFixtures.ActivateAsync(provider, "shared", version: 3, tenant);
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = tenant,
            ResourceId = "shared",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<List<int>> ReadVersionsAsync(IServiceProvider provider, TenantScope tenant) =>
        (await PolicyTestFixtures.ReadVersionsAsync(provider, "shared", tenant)).Select(static version => version.Version).ToList();

    private static async Task<List<int>> ReadActiveVersionsAsync(IServiceProvider provider, TenantScope tenant)
    {
        var reader = provider.GetRequiredService<IResourceVersionReader>();
        return (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
            ResourceIds = ["shared"],
        })).Select(static version => version.Version).Order().ToList();
    }
}

public sealed class SqliteJsonPolicyPruningApplicationTests() : PolicyPruningApplicationStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbPolicyPruningApplicationTests() : PolicyPruningApplicationStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonPolicyPruningApplicationTests() : PolicyPruningApplicationStoreTests(new SqlServerTestDatabase());
