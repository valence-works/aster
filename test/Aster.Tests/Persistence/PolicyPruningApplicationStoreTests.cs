using Aster.Core.Abstractions;
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
