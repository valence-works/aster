using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class ResourceLifecycleHookDispatcherTests
{
    [Fact]
    public async Task InvokeBeforeSaveAsync_RunsHooksInRegistrationOrder()
    {
        var recorder = new LifecycleHookRecorder();
        var dispatcher = CreateDispatcher(new FirstRecordingHook(recorder), new SecondRecordingHook(recorder));

        await dispatcher.InvokeBeforeSaveAsync(CreateSaveContext(LifecyclePoint.BeforeSave));

        Assert.Equal(["first", "second"], recorder.Events.Select(static e => e.HookName).ToList());
    }

    [Fact]
    public async Task InvokeBeforeSaveAsync_RejectionStopsLaterHooks()
    {
        var recorder = new LifecycleHookRecorder { RejectAt = LifecyclePoint.BeforeSave };
        var dispatcher = CreateDispatcher(new FirstRecordingHook(recorder), new SecondRecordingHook(recorder));

        var exception = await Assert.ThrowsAsync<LifecycleHookException>(() =>
            dispatcher.InvokeBeforeSaveAsync(CreateSaveContext(LifecyclePoint.BeforeSave)).AsTask());

        Assert.Equal("test-rejected", exception.Code);
        Assert.Equal(LifecyclePoint.BeforeSave, exception.LifecyclePoint);
        var onlyEvent = Assert.Single(recorder.Events);
        Assert.Equal("first", onlyEvent.HookName);
    }

    [Fact]
    public async Task InvokeBeforeSaveAsync_FailedOutcomePreservesHookCode()
    {
        var recorder = new LifecycleHookRecorder { FailAt = LifecyclePoint.BeforeSave };
        var dispatcher = CreateDispatcher(new FirstRecordingHook(recorder));

        var exception = await Assert.ThrowsAsync<LifecycleHookException>(() =>
            dispatcher.InvokeBeforeSaveAsync(CreateSaveContext(LifecyclePoint.BeforeSave)).AsTask());

        Assert.Equal("test-failed", exception.Code);
        Assert.Equal(LifecyclePoint.BeforeSave, exception.LifecyclePoint);
    }

    [Fact]
    public async Task InvokeAfterSaveAsync_WrapsHookFailures()
    {
        var recorder = new LifecycleHookRecorder { ThrowAt = LifecyclePoint.AfterSave };
        var dispatcher = CreateDispatcher(new FirstRecordingHook(recorder));

        var exception = await Assert.ThrowsAsync<LifecycleHookException>(() =>
            dispatcher.InvokeAfterSaveAsync(CreateSaveContext(LifecyclePoint.AfterSave)).AsTask());

        Assert.Equal(LifecycleHookException.FailedCode, exception.Code);
        Assert.Equal(LifecyclePoint.AfterSave, exception.LifecyclePoint);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task InvokeBeforeSaveAsync_PreservesCancellation()
    {
        var recorder = new LifecycleHookRecorder { CancelAt = LifecyclePoint.BeforeSave };
        var dispatcher = CreateDispatcher(new FirstRecordingHook(recorder));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dispatcher.InvokeBeforeSaveAsync(CreateSaveContext(LifecyclePoint.BeforeSave)).AsTask());
    }

    private static ResourceSaveLifecycleContext CreateSaveContext(LifecyclePoint lifecyclePoint) =>
        new()
        {
            OperationId = Guid.NewGuid(),
            LifecyclePoint = lifecyclePoint,
            CancellationToken = CancellationToken.None,
            SaveKind = ResourceSaveKind.Create,
            DefinitionId = LifecycleHookTestFixtures.DefinitionId,
            ResourceId = LifecycleHookTestFixtures.ResourceId,
        };

    private static ResourceLifecycleHookDispatcher CreateDispatcher(params IResourceLifecycleHook[] hooks)
    {
        var services = new ServiceCollection();
        foreach (var hook in hooks)
            services.AddSingleton(hook);

        return new ResourceLifecycleHookDispatcher(services.BuildServiceProvider());
    }
}
