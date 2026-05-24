using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.InMemory;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public sealed class TenantApiOverloadCompatibilityTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();
    private readonly IResourceDefinitionStore definitions;
    private readonly IResourceManager manager;

    public TenantApiOverloadCompatibilityTests()
    {
        definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        manager = provider.GetRequiredService<IResourceManager>();
    }

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task LegacyOverloads_AcceptDefaultLiteralWithoutTenantAmbiguity()
    {
        await definitions.RegisterDefinitionAsync(
            new ResourceDefinitionBuilder()
                .WithDefinitionId("Product")
                .Build(),
            default);

        await TenantScopeTestFixtures.CreateProductAsync(provider, "product-1", "Default");
        await manager.ActivateAsync("product-1", 1, "Published", default);

        var latest = await manager.GetLatestVersionAsync("product-1", default);
        var active = (await manager.GetActiveVersionsAsync("product-1", "Published", default)).ToList();

        Assert.NotNull(await definitions.GetDefinitionAsync("Product", default));
        Assert.Equal(1, latest!.Version);
        Assert.Equal(1, active.Single().Version);
    }

    [Fact]
    public async Task ConcreteResourceManagers_AcceptDefaultLiteralWithoutTenantAmbiguity()
    {
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product-1", "Default");
        await manager.ActivateAsync("product-1", 1, "Published");

        var defaultManager = provider.GetRequiredService<DefaultResourceManager>();
        var inMemoryManager = provider.GetRequiredService<InMemoryResourceManager>();

        Assert.Single(await defaultManager.GetVersionsAsync("product-1", default));
        Assert.Single(await inMemoryManager.GetVersionsAsync("product-1", default));
        Assert.NotNull(await defaultManager.GetLatestVersionAsync("product-1", default));
        Assert.NotNull(await inMemoryManager.GetLatestVersionAsync("product-1", default));
        Assert.Single(await defaultManager.GetActiveVersionsAsync("product-1", "Published", default));
        Assert.Single(await inMemoryManager.GetActiveVersionsAsync("product-1", "Published", default));
    }
}
