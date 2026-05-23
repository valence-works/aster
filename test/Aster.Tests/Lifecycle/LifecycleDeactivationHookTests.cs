using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleDeactivationHookTests : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IResourceManager manager;
    private readonly LifecycleHookRecorder recorder;

    public LifecycleDeactivationHookTests()
    {
        provider = LifecycleHookTestFixtures.BuildServices(
            typeof(FirstRecordingHook),
            typeof(SecondRecordingHook));
        manager = provider.GetRequiredService<IResourceManager>();
        recorder = provider.GetRequiredService<LifecycleHookRecorder>();
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    [Fact]
    public async Task DeactivateAsync_RunsHooksInOrderWithResultingActiveVersions()
    {
        var resource = await CreateActiveResourceAsync();
        recorder.Events.Clear();

        await manager.DeactivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel);

        Assert.Equal(
            [
                ("first", LifecyclePoint.BeforeDeactivate),
                ("second", LifecyclePoint.BeforeDeactivate),
                ("first", LifecyclePoint.AfterDeactivate),
                ("second", LifecyclePoint.AfterDeactivate),
            ],
            recorder.Events.Select(static e => (e.HookName, e.LifecyclePoint)).ToList());

        var before = Assert.IsType<ResourceActivationLifecycleContext>(recorder.Events[0].Context);
        var after = Assert.IsType<ResourceActivationLifecycleContext>(recorder.Events[2].Context);
        Assert.Empty(before.ActiveVersions);
        Assert.Empty(after.ActiveVersions);
        Assert.Equal(before.OperationId, after.OperationId);
    }

    [Fact]
    public async Task BeforeDeactivationRejection_LeavesChannelStateUnchanged()
    {
        var resource = await CreateActiveResourceAsync();
        recorder.Events.Clear();
        recorder.RejectAt = LifecyclePoint.BeforeDeactivate;

        await Assert.ThrowsAsync<LifecycleHookException>(() =>
            manager.DeactivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel).AsTask());

        var active = await manager.GetActiveVersionsAsync(resource.ResourceId, LifecycleHookTestFixtures.Channel);
        var activeResource = Assert.Single(active);
        Assert.Equal(resource.Version, activeResource.Version);
    }

    [Fact]
    public static async Task DeactivateAsync_HooksCannotMutatePersistedActiveVersions()
    {
        await using var scopedProvider = LifecycleHookTestFixtures.BuildServices(typeof(MutatingActivationVersionsHook));
        var scopedManager = scopedProvider.GetRequiredService<IResourceManager>();
        await LifecycleHookTestFixtures.SaveDefinitionAsync(scopedProvider);
        var resource = await scopedManager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        await scopedManager.ActivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel);

        await scopedManager.DeactivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel);

        Assert.Empty(await scopedManager.GetActiveVersionsAsync(resource.ResourceId, LifecycleHookTestFixtures.Channel));
    }

    private async ValueTask<Resource> CreateActiveResourceAsync()
    {
        await LifecycleHookTestFixtures.SaveDefinitionAsync(provider);
        var resource = await manager.CreateAsync(
            LifecycleHookTestFixtures.DefinitionId,
            LifecycleHookTestFixtures.CreateRequest());
        await manager.ActivateAsync(resource.ResourceId, resource.Version, LifecycleHookTestFixtures.Channel);
        return resource;
    }
}
