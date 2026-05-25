using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class LifecycleMarkerConflictTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_DifferentMarkerState_ReturnsConflictDiagnostic()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var service = provider.GetRequiredService<IResourceLifecycleMarkerService>();

        await service.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
        var conflict = await service.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.SoftDeleted,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        Assert.False(conflict.Succeeded);
        var diagnostic = Assert.Single(conflict.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, diagnostic.Code);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, conflict.Marker!.State);
    }
}
