using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleSaveHookTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceManager manager;
    private readonly LifecycleHookRecorder recorder;

    public LifecycleSaveHookTests()
    {
        provider = LifecycleHookTestFixtures.BuildServices(
            typeof(FirstRecordingHook),
            typeof(SecondRecordingHook));
        manager = provider.GetRequiredService<IResourceManager>();
        recorder = provider.GetRequiredService<LifecycleHookRecorder>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task CreateAsync_RunsSaveHooksInOrderWithContext()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);

        var resource = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());

        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeSave),
                ("second", LifecyclePoint.BeforeSave),
                ("first", LifecyclePoint.AfterSave),
                ("second", LifecyclePoint.AfterSave),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var before = Assert.IsType<ResourceSaveLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceSaveLifecycleContext>(recorder.Events[2].Context);
        Assert.Equal(ResourceSaveKind.Create, before.SaveKind);
        Assert.Equal(resource.ResourceId, before.ResourceId);
        Assert.Equal(resource.ResourceId, after.ResourceId);
        Assert.Equal(resource.Version, after.Resource!.Version);
        Assert.Equal(before.OperationId, after.OperationId);
    }

    [Fact]
    public async Task UpdateAsync_RunsSaveHooksWithBaseVersionContext()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var created = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        recorder.Events.Clear();

        var updated = await manager.UpdateAsync(
            created.ResourceId,
            LifecycleHookTestFixtures.UpdateRequest(created.Version));

        var before = Assert.IsType<ResourceSaveLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceSaveLifecycleContext>(recorder.Events[2].Context);
        Assert.Equal(ResourceSaveKind.Update, before.SaveKind);
        Assert.Equal(created.Version, before.BaseVersion);
        Assert.Equal(updated.Version, after.Resource!.Version);
        Assert.Equal(before.OperationId, after.OperationId);
    }

    [Fact]
    public async Task BeforeSaveRejection_PreventsCreateAndUpdatePersistence()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        recorder.RejectAt = LifecyclePoint.BeforeSave;

        await Assert.ThrowsAsync<LifecycleHookException>(() =>
            manager.CreateAsync(
                LifecycleHookTestFixtures.DefinitionId,
                LifecycleHookTestFixtures.CreateRequest()).AsTask());
        Assert.Null(await manager.GetLatestVersionAsync(LifecycleHookTestFixtures.ResourceId));

        recorder.RejectAt = null;
        var created = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        recorder.RejectAt = LifecyclePoint.BeforeSave;

        await Assert.ThrowsAsync<LifecycleHookException>(() =>
            manager.UpdateAsync(created.ResourceId, LifecycleHookTestFixtures.UpdateRequest(created.Version)).AsTask());
        var versions = (await manager.GetVersionsAsync(created.ResourceId)).ToList();
        Assert.Single(versions);
    }

    [Fact]
    public async Task AfterSaveFailure_IsVisibleAfterPersistence()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        recorder.FailAt = LifecyclePoint.AfterSave;

        var exception = await Assert.ThrowsAsync<LifecycleHookException>(() =>
            manager.CreateAsync(
                LifecycleHookTestFixtures.DefinitionId,
                LifecycleHookTestFixtures.CreateRequest()).AsTask());

        Assert.Equal(LifecycleHookException.FailedCode, exception.Code);
        Assert.NotNull(await manager.GetLatestVersionAsync(LifecycleHookTestFixtures.ResourceId));
    }

    [Fact]
    public static async Task SaveHooks_CannotMutatePersistedResourceAspects()
    {
        await using var scopedProvider = LifecycleHookTestFixtures.BuildServices(typeof(MutatingSaveResourceHook));
        var scopedManager = scopedProvider.GetRequiredService<IResourceManager>();
        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider);

        var created = await scopedManager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            new CreateResourceRequest
            {
                ResourceId = LifecycleHookTestFixtures.ResourceId,
                InitialAspects = new Dictionary<string, object>
                {
                    ["title"] = "Initial",
                    ["details"] = new Dictionary<string, object>
                    {
                        ["name"] = "Original nested value",
                    },
                    ["tags"] = new List<string> { "stable" },
                },
            });
        var updated = await scopedManager.UpdateAsync(
            created.ResourceId,
            LifecycleHookTestFixtures.UpdateRequest(created.Version));

        Assert.Equal("Initial", created.Aspects["title"]);
        var details = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(created.Aspects["details"]);
        Assert.Equal("Original nested value", details["name"]);
        Assert.Equal(["stable"], Assert.IsAssignableFrom<IReadOnlyList<string>>(created.Aspects["tags"]));
        Assert.Equal("Updated", updated.Aspects["title"]);
        Assert.Equal("Updated", (await scopedManager.GetLatestVersionAsync(created.ResourceId))!.Aspects["title"]);
    }
}
