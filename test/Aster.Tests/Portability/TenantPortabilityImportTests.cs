using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Portability;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class TenantPortabilityImportTests : IDisposable
{
    private readonly ServiceProvider provider = TenantScopeTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ImportAsync_WritesSnapshotIntoExplicitTargetTenant()
    {
        var portability = provider.GetRequiredService<IResourcePortabilityService>();
        var reader = provider.GetRequiredService<IResourceVersionReader>();
        var snapshot = CreateSnapshot();

        var result = await portability.ImportAsync(snapshot, new PortableImportOptions
        {
            TargetTenantScope = TenantScopeTestFixtures.TenantB,
        });

        var targetResources = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
        })).ToList();
        var sourceResources = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
        })).ToList();

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Equal(TenantScopeTestFixtures.TenantA, result.SourceTenantScope);
        Assert.Equal(TenantScopeTestFixtures.TenantB, result.TargetTenantScope);
        Assert.Single(targetResources);
        Assert.Empty(sourceResources);
    }

    private static PortableSnapshot CreateSnapshot() =>
        new()
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            SourceTenantScope = TenantScopeTestFixtures.TenantA,
            Definitions =
            [
                new ResourceDefinitionBuilder()
                    .WithDefinitionId("Product")
                    .Build() with { Version = 1, TenantScope = TenantScopeTestFixtures.TenantA },
            ],
            Resources =
            [
                TenantScopeTestFixtures.CreateResource("imported-product", "Imported", TenantScopeTestFixtures.TenantA),
            ],
        };
}
