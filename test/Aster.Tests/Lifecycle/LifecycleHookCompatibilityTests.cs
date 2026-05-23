using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleHookCompatibilityTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    public LifecycleHookCompatibilityTests()
    {
        provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task AddAsterCore_WithNoHooks_PreservesLifecycleOperations()
    {
        Assert.Empty(provider.GetServices<IResourceLifecycleHook>());

        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var manager = provider.GetRequiredService<IResourceManager>();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        var created = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        var updated = await manager.UpdateAsync(
            created.ResourceId,
            LifecycleHookTestFixtures.UpdateRequest(created.Version));
        await manager.ActivateAsync(updated.ResourceId, updated.Version, LifecycleHookTestFixtures.Channel);

        var export = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [updated.ResourceId],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });
        Assert.NotNull(export.Snapshot);

        var preview = await portability.PreviewImportAsync(export.Snapshot);
        Assert.True(preview.CanImport);

        var import = await portability.ImportAsync(export.Snapshot);
        Assert.Equal(PortableImportStatus.NoOp, import.Status);
    }
}
