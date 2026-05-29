using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleRestoreActivationTests : IDisposable
{
    private readonly ServiceProvider provider = LifecycleRestoreTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task RestoreAsync_DoesNotRewriteVersionsOrChangeActivationState()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.CreateAsync("Product", new CreateResourceRequest { ResourceId = "product-1" });
        await manager.UpdateAsync("product-1", new UpdateResourceRequest { BaseVersion = 1 });
        await manager.ActivateAsync("product-1", 2, "Published");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "product-1", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("product-1", ResourceLifecycleMarkerState.Archived)],
        });

        var versions = (await manager.GetVersionsAsync("product-1")).OrderBy(static resource => resource.Version).ToList();
        var active = (await manager.GetActiveVersionsAsync("product-1", "Published")).ToList();

        Assert.Equal([1, 2], versions.Select(static resource => resource.Version).ToList());
        var activeVersion = Assert.Single(active);
        Assert.Equal(2, activeVersion.Version);
    }
}
