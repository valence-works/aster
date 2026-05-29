using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Lifecycle;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Querying;

public sealed class LifecycleStateQueryTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task QueryAsync_WithLifecycleState_ReturnsMatchingResourcesOnly()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "archived");
        await PolicyTestFixtures.SaveResourceAsync(provider, "unmarked");
        var markerService = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        await markerService.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "archived",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
        var query = provider.GetRequiredService<IResourceQueryService>();

        var archived = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
            Sorts = [new SortExpression("ResourceId")],
        })).ToList();
        var unmarked = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.None,
            Sorts = [new SortExpression("ResourceId")],
        })).ToList();

        Assert.Equal(["archived"], archived.Select(static resource => resource.ResourceId).ToList());
        Assert.Equal(["unmarked"], unmarked.Select(static resource => resource.ResourceId).ToList());
    }

    [Fact]
    public async Task QueryAsync_AfterRestore_DoesNotReturnRestoredLifecycleState()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "archived");
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "archived",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
        await provider.GetRequiredService<IResourceLifecycleRestoreService>().RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived)],
        });
        var query = provider.GetRequiredService<IResourceQueryService>();

        var archived = (await query.QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        Assert.Empty(archived);
    }
}
