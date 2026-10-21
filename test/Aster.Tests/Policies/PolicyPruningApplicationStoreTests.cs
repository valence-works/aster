using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyPruningApplicationStoreTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PruneVersionAsync_RemovesOnlyMatchingVersion()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        var pruningStore = provider.GetRequiredService<IResourceVersionPruningStore>();

        var removed = await pruningStore.PruneVersionAsync("versioned", 1, TenantScope.Default);
        var removedAgain = await pruningStore.PruneVersionAsync("versioned", 1, TenantScope.Default);

        Assert.True(removed);
        Assert.False(removedAgain);
        Assert.Equal([2], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());
    }

    [Fact]
    public async Task PruneVersionAsync_FailsClosedForLatestVersion()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        var pruningStore = provider.GetRequiredService<IResourceVersionPruningStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pruningStore.PruneVersionAsync("versioned", 2, TenantScope.Default));

        Assert.Equal([1, 2], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());
    }

    [Fact]
    public async Task PruneVersionAsync_FailsClosedForActiveVersion()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 1);
        await PolicyTestFixtures.SaveResourceAsync(provider, "versioned", version: 2);
        await PolicyTestFixtures.ActivateAsync(provider, "versioned", version: 1);
        var pruningStore = provider.GetRequiredService<IResourceVersionPruningStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pruningStore.PruneVersionAsync("versioned", 1, TenantScope.Default));

        Assert.Equal([1, 2], (await PolicyTestFixtures.ReadVersionsAsync(provider, "versioned")).Select(static version => version.Version).ToList());
    }

    [Fact]
    public async Task PruneVersionAsync_WithAFloor_RefusesTheDeleteThatWouldBreakIt()
    {
        // v4 is latest and v3 active; each prune counts what would remain, active versions only when the floor says.
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
}
