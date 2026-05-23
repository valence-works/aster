using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleActivationHookTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceManager manager;
    private readonly LifecycleHookRecorder recorder;

    public LifecycleActivationHookTests()
    {
        provider = LifecycleHookTestFixtures.BuildServices(
            typeof(FirstRecordingHook),
            typeof(SecondRecordingHook));
        manager = provider.GetRequiredService<IResourceManager>();
        recorder = provider.GetRequiredService<LifecycleHookRecorder>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task ActivateAsync_RunsHooksInOrderWithResultingActiveVersions()
    {
        var resource = await CreateTwoVersionResourceWithFirstVersionActiveAsync();
        recorder.Events.Clear();

        await manager.ActivateAsync(
            resource.ResourceId,
            resource.Version,
            LifecycleHookTestFixtures.Channel,
            allowMultipleActive: true);

        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeActivate),
                ("second", LifecyclePoint.BeforeActivate),
                ("first", LifecyclePoint.AfterActivate),
                ("second", LifecyclePoint.AfterActivate),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var before = Assert.IsType<ResourceActivationLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceActivationLifecycleContext>(recorder.Events[2].Context);
        Assert.True(before.AllowMultipleActive);
        Assert.Equal([1, 2], before.ActiveVersions);
        Assert.Equal([1, 2], after.ActiveVersions);
        Assert.Equal(before.OperationId, after.OperationId);
    }

    [Fact]
    public async Task BeforeActivationRejection_LeavesChannelStateUnchanged()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var resource = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        recorder.Events.Clear();
        recorder.RejectAt = LifecyclePoint.BeforeActivate;

        await Assert.ThrowsAsync<LifecycleHookException>(() =>
            manager.ActivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel).AsTask());

        Assert.Empty(await manager.GetActiveVersionsAsync(resource.ResourceId, LifecycleHookTestFixtures.Channel));
    }

    private async ValueTask<Resource> CreateTwoVersionResourceWithFirstVersionActiveAsync()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var v1 = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        await manager.ActivateAsync(v1.ResourceId, v1.Version, LifecycleHookTestFixtures.Channel);
        return await manager.UpdateAsync(v1.ResourceId, LifecycleHookTestFixtures.UpdateRequest(v1.Version));
    }
}
