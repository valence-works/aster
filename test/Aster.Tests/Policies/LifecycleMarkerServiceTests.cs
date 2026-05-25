using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class LifecycleMarkerServiceTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_WritesMarkerAndRepeatedSameMarkerIsIdempotent()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var service = provider.GetRequiredService<IResourceLifecycleMarkerService>();

        var first = await service.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        });
        var second = await service.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = new DateTimeOffset(2026, 5, 26, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(first.Marker, second.Marker);
    }
}
