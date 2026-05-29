using Aster.Core.Abstractions;
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
}
