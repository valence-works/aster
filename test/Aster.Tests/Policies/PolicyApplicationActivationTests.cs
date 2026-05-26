using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyApplicationActivationTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_DoesNotChangeActivationState()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "active-product");
        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.ActivateAsync("active-product", 1, "Published");

        await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("active-product")],
        });

        var active = await manager.GetActiveVersionsAsync("active-product", "Published");
        var version = Assert.Single(active);
        Assert.Equal(1, version.Version);
    }
}
