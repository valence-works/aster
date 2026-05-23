using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleSchemaUpgradeHookTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceManager manager;
    private readonly IResourceSchemaVersionService schemaVersions;
    private readonly LifecycleHookRecorder recorder;

    public LifecycleSchemaUpgradeHookTests()
    {
        provider = LifecycleHookTestFixtures.BuildServices(typeof(FirstRecordingHook));
        manager = provider.GetRequiredService<IResourceManager>();
        schemaVersions = provider.GetRequiredService<IResourceSchemaVersionService>();
        recorder = provider.GetRequiredService<LifecycleHookRecorder>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task UpgradeAsync_RunsSchemaUpgradeSaveHooks()
    {
        var created = await CreateUpgradeableResourceAsync();
        recorder.Events.Clear();

        var result = await schemaVersions.UpgradeAsync(created.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = created.Version,
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Equal([LifecyclePoint.BeforeSave, LifecyclePoint.AfterSave], recorder.Events.Select(static e => e.LifecyclePoint));

        var before = Assert.IsType<ResourceSaveLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceSaveLifecycleContext>(recorder.Events[1].Context);
        Assert.Equal(ResourceSaveKind.SchemaUpgrade, before.SaveKind);
        Assert.Equal(created.Version, before.BaseVersion);
        Assert.Equal(2, after.Resource!.Version);
        Assert.Equal(before.OperationId, after.OperationId);
    }

    [Fact]
    public async Task BeforeSaveRejection_PreventsSchemaUpgradePersistence()
    {
        var created = await CreateUpgradeableResourceAsync();
        recorder.Events.Clear();
        recorder.RejectAt = LifecyclePoint.BeforeSave;

        await Assert.ThrowsAsync<LifecycleHookException>(() =>
            schemaVersions.UpgradeAsync(created.ResourceId, new ResourceSchemaUpgradeRequest
            {
                BaseVersion = created.Version,
            }).AsTask());

        var versions = (await manager.GetVersionsAsync(created.ResourceId)).ToList();
        Assert.Single(versions);
    }

    [Fact]
    public static async Task UpgradeAsync_HooksCannotMutatePersistedResourceAspects()
    {
        await using var scopedProvider = LifecycleHookTestFixtures.BuildServices(typeof(MutatingSaveResourceHook));
        var scopedManager = scopedProvider.GetRequiredService<IResourceManager>();
        var scopedSchemaVersions = scopedProvider.GetRequiredService<IResourceSchemaVersionService>();
        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider);
        var created = await scopedManager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider, version: 2);

        var result = await scopedSchemaVersions.UpgradeAsync(created.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = created.Version,
        });
        var upgraded = result.Resource!;

        Assert.Equal("Initial", upgraded.Aspects["title"]);
        Assert.Equal("Initial", (await scopedManager.GetLatestVersionAsync(created.ResourceId))!.Aspects["title"]);
    }

    private async ValueTask<Resource> CreateUpgradeableResourceAsync()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var created = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider, version: 2);
        return created;
    }
}
