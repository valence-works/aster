using Aster.Core.Exceptions;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Services;

namespace Aster.Tests.Lifecycle;

public sealed class ResourceLifecycleHookDispatcherTests
{
    [Fact]
    public async Task InvokeBeforeSaveAsync_RunsHooksInRegistrationOrder()
    {
        var recorder = new LifecycleHookRecorder();
        var dispatcher = new ResourceLifecycleHookDispatcher(
            [new FirstRecordingHook(recorder), new SecondRecordingHook(recorder)]);

        await dispatcher.InvokeBeforeSaveAsync(CreateSaveContext(LifecyclePoint.BeforeSave));

        Assert.Equal(["first", "second"], recorder.Events.Select(static e => e.HookName).ToList());
    }

    [Fact]
    public async Task InvokeBeforeSaveAsync_RejectionStopsLaterHooks()
    {
        var recorder = new LifecycleHookRecorder { RejectAt = LifecyclePoint.BeforeSave };
        var dispatcher = new ResourceLifecycleHookDispatcher(
            [new FirstRecordingHook(recorder), new SecondRecordingHook(recorder)]);

        var exception = await Assert.ThrowsAsync<LifecycleHookException>(() =>
            dispatcher.InvokeBeforeSaveAsync(CreateSaveContext(LifecyclePoint.BeforeSave)).AsTask());

        Assert.Equal(LifecycleHookException.RejectedCode, exception.Code);
        Assert.Equal(LifecyclePoint.BeforeSave, exception.LifecyclePoint);
        var onlyEvent = Assert.Single(recorder.Events);
        Assert.Equal("first", onlyEvent.HookName);
    }

    [Fact]
    public async Task InvokeAfterSaveAsync_WrapsHookFailures()
    {
        var recorder = new LifecycleHookRecorder { ThrowAt = LifecyclePoint.AfterSave };
        var dispatcher = new ResourceLifecycleHookDispatcher([new FirstRecordingHook(recorder)]);

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
        var dispatcher = new ResourceLifecycleHookDispatcher([new FirstRecordingHook(recorder)]);

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
}
