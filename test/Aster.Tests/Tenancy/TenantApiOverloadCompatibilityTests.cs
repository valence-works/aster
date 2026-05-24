using Aster.Core.Abstractions;
using Aster.Core.Definitions;
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
}
