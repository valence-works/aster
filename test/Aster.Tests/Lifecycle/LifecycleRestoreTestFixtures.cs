using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

internal static class LifecycleRestoreTestFixtures
{
    public static ServiceProvider CreateCoreProvider() => PolicyTestFixtures.CreateCoreProvider();

    public static ServiceProvider CreateSqliteProvider(string databasePath) =>
        PolicyTestFixtures.CreateSqliteProvider(databasePath);

    public static async Task SaveProductAsync(
        IServiceProvider provider,
        string resourceId,
        TenantScope? tenantScope = null,
        int version = 1)
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, tenantScope);
        await PolicyTestFixtures.SaveResourceAsync(provider, resourceId, version: version, tenantScope: tenantScope);
    }

    public static async Task MarkAsync(
        IServiceProvider provider,
        string resourceId,
        ResourceLifecycleMarkerState state,
        TenantScope? tenantScope = null)
    {
        var markers = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = tenantScope,
            ResourceId = resourceId,
            State = state,
            MarkedAt = new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero),
        });
    }

    public static ResourceLifecycleRestoreCandidate Candidate(
        string? resourceId,
        ResourceLifecycleMarkerState? expectedState) =>
        new()
        {
            ResourceId = resourceId,
            ExpectedState = expectedState,
        };

    public static async Task<ResourceLifecycleMarker?> ReadMarkerAsync(
        IServiceProvider provider,
        string resourceId,
        TenantScope? tenantScope = null)
    {
        var store = provider.GetRequiredService<IResourceLifecycleMarkerStore>();
        return await store.GetMarkerAsync(resourceId, tenantScope ?? TenantScope.Default);
    }
}
