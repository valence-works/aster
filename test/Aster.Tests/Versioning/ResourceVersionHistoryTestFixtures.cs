using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Versioning;

internal static class ResourceVersionHistoryTestFixtures
{
    public static ServiceProvider CreateCoreProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

    public static async Task SaveVersionsAsync(
        IServiceProvider provider,
        string resourceId,
        int versionCount,
        TenantScope? tenantScope = null)
    {
        for (var version = 1; version <= versionCount; version++)
            await SaveVersionAsync(provider, resourceId, version, tenantScope);
    }

    public static async Task SaveVersionAsync(
        IServiceProvider provider,
        string resourceId,
        int version,
        TenantScope? tenantScope = null)
    {
        await provider.GetRequiredService<IResourceVersionWriter>().SaveVersionAsync(new Resource
        {
            TenantScope = tenantScope ?? TenantScope.Default,
            ResourceId = resourceId,
            Id = $"{(tenantScope ?? TenantScope.Default).TenantId}-{resourceId}-{version}",
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = version,
            Created = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc).AddMinutes(version),
        });
    }

    public static async Task ActivateAsync(
        IServiceProvider provider,
        string resourceId,
        string channel,
        IReadOnlyList<int> versions,
        TenantScope? tenantScope = null)
    {
        var tenant = tenantScope ?? TenantScope.Default;
        await provider.GetRequiredService<IResourceVersionWriter>().UpdateActivationAsync(resourceId, channel, new ActivationState
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = versions,
            LastUpdated = DateTime.UtcNow,
        });
    }

    public static async Task MarkAsync(
        IServiceProvider provider,
        string resourceId,
        ResourceLifecycleMarkerState state,
        TenantScope? tenantScope = null)
    {
        await provider.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = tenantScope,
            ResourceId = resourceId,
            State = state,
            MarkedAt = DateTimeOffset.UtcNow,
        });
    }
}
