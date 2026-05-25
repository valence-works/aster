using Aster.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class PolicyDefinitionStoreTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task RegisterDefinitionAsync_PreservesPolicyDeclarations()
    {
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        await store.RegisterDefinitionAsync(PolicyTestFixtures.ProductDefinition(PolicyTestFixtures.ArchivePolicy()));

        var definition = await store.GetDefinitionAsync("Product");

        Assert.NotNull(definition);
        var policy = Assert.Single(definition.PolicyDeclarations);
        Assert.Equal("archive-old", policy.PolicyId);
    }
}
