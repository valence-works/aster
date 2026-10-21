using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Persistence;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqlServerJson;

/// <summary>
/// SQL Server compares strings as if padded with trailing spaces, even under a binary collation, so <c>"x"</c> and
/// <c>"x "</c> would be one key. Identifiers with trailing spaces are refused, and an upsert never treats one as
/// another's row. A lookup, update or delete under a padded tenant ID is refused, so it can't reach the tenant
/// without the space.
/// </summary>
public sealed class SqlServerJsonIdentifierTests : IDisposable
{
    private readonly SqlServerTestDatabase database = new();

    public void Dispose() => database.Dispose();

    [SkippableFact]
    public async Task CreateAsync_ResourceIdWithTrailingSpace_IsRefused()
    {
        await using var provider = database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);

        await Assert.ThrowsAnyAsync<Exception>(() => TenantScopeTestFixtures.CreateProductAsync(provider, "padded ", "v1"));

        Assert.Null(await provider.GetRequiredService<IResourceManager>().GetLatestVersionAsync("padded"));
    }

    [SkippableFact]
    public async Task UpdateActivationAsync_IdDifferingOnlyByTrailingSpace_DoesNotOverwriteTheOtherRow()
    {
        await using var provider = database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product", "v1");
        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.ActivateAsync("product", 1, "production");
        var writer = provider.GetRequiredService<IResourceVersionWriter>();

        await Assert.ThrowsAnyAsync<Exception>(() => writer.UpdateActivationAsync("product ", "production", new ActivationState
        {
            ResourceId = "product ",
            Channel = "production",
            ActiveVersions = [],
            LastUpdated = DateTime.UtcNow,
        }).AsTask());

        Assert.Equal([1], (await manager.GetActiveVersionsAsync("product", "production")).Select(static resource => resource.Version));
    }

    // Constructed with an initializer, as a host's own tenant accessor might, so no core rule refuses it first.
    private static readonly TenantScope PaddedTenantA = new() { TenantId = "tenant-a " };

    [SkippableFact]
    public async Task Reads_UnderTenantIdWithTrailingSpace_AreRefused()
    {
        await using var provider = database.CreateProvider();
        await SeedTenantAAsync(provider);
        var manager = provider.GetRequiredService<IResourceManager>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.GetLatestVersionAsync("product", PaddedTenantA, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.GetActiveVersionsAsync("product", "production", PaddedTenantA, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<IResourceQueryService>()
                .QueryAsync(TenantScopeTestFixtures.ProductQuery(PaddedTenantA)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<IResourceLifecycleMarkerStore>()
                .GetMarkerAsync("product", PaddedTenantA).AsTask());
    }

    [SkippableFact]
    public async Task Update_UnderTenantIdWithTrailingSpace_LeavesTheOtherTenantUnchanged()
    {
        await using var provider = database.CreateProvider();
        await SeedTenantAAsync(provider);
        var writer = provider.GetRequiredService<IResourceVersionWriter>();

        await Assert.ThrowsAsync<ArgumentException>(() => writer.UpdateActivationAsync("product", "production", new ActivationState
        {
            TenantScope = PaddedTenantA,
            ResourceId = "product",
            Channel = "production",
            ActiveVersions = [],
            LastUpdated = DateTime.UtcNow,
        }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<IResourceLifecycleMarkerStore>().SaveMarkerAsync(new ResourceLifecycleMarker
            {
                TenantScope = PaddedTenantA,
                ResourceId = "product",
                State = ResourceLifecycleMarkerState.SoftDeleted,
                MarkedAt = DateTimeOffset.UtcNow,
            }).AsTask());

        var manager = provider.GetRequiredService<IResourceManager>();
        Assert.Equal(
            [1],
            (await manager.GetActiveVersionsAsync("product", "production", TenantScopeTestFixtures.TenantA, CancellationToken.None))
                .Select(static resource => resource.Version));
        var marker = await provider.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("product", TenantScopeTestFixtures.TenantA);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, marker?.State);
    }

    [SkippableFact]
    public async Task Delete_UnderTenantIdWithTrailingSpace_LeavesTheOtherTenantUnchanged()
    {
        await using var provider = database.CreateProvider();
        await SeedTenantAAsync(provider);
        var markers = provider.GetRequiredService<IResourceLifecycleMarkerClearStore>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            markers.ClearMarkerAsync("product", PaddedTenantA, ResourceLifecycleMarkerState.Archived).AsTask());

        Assert.Equal(
            ResourceLifecycleMarkerState.Archived,
            (await markers.GetMarkerAsync("product", TenantScopeTestFixtures.TenantA))?.State);
    }

    // Tenant A holds an activated, archived product.
    private static async Task SeedTenantAAsync(IServiceProvider provider)
    {
        var tenant = TenantScopeTestFixtures.TenantA;
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider, tenant);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product", "v1", tenant);
        await provider.GetRequiredService<IResourceManager>()
            .ActivateAsync("product", 1, "production", tenant, allowMultipleActive: false, CancellationToken.None);
        await provider.GetRequiredService<IResourceLifecycleMarkerStore>().SaveMarkerAsync(new ResourceLifecycleMarker
        {
            TenantScope = tenant,
            ResourceId = "product",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = DateTimeOffset.UtcNow,
        });
    }
}
