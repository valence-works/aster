using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public static class LifecycleHookTestFixtures
{
    public const string DefinitionId = "article";
    public const string ResourceId = "article-1";
    public const string Channel = "published";

    public static ServiceProvider BuildServices(params Type[] hookTypes)
    {
        var services = new ServiceCollection()
            .AddSingleton<LifecycleHookRecorder>()
            .AddAsterCore();

        foreach (var hookType in hookTypes)
        {
            services.AddSingleton(hookType);
            services.AddSingleton(typeof(IResourceLifecycleHook), sp => sp.GetRequiredService(hookType));
        }

        return services.BuildServiceProvider();
    }

    public static async ValueTask<ResourceDefinition> SaveDefinitionAsync(IServiceProvider services, int version = 1)
    {
        var definition = Definition(version);
        var store = services.GetRequiredService<IResourceDefinitionStore>();
        await store.RegisterDefinitionAsync(definition);
        return (await store.GetDefinitionAsync(definition.DefinitionId))!;
    }

    public static ResourceDefinition Definition(int version = 1) =>
        new()
        {
            DefinitionId = DefinitionId,
            Id = $"definition-{version}",
            Version = version,
            AspectDefinitions = new Dictionary<string, AspectDefinition>
            {
                ["title"] = new()
                {
                    AspectDefinitionId = "title",
                    Id = $"title-{version}",
                    Version = version,
                    Schema = """{"type":"object"}""",
                },
            },
        };

    public static CreateResourceRequest CreateRequest(string? resourceId = ResourceId) =>
        new()
        {
            ResourceId = resourceId,
            InitialAspects = new Dictionary<string, object>
            {
                ["title"] = "Initial",
            },
        };

    public static UpdateResourceRequest UpdateRequest(int baseVersion, string title = "Updated") =>
        new()
        {
            BaseVersion = baseVersion,
            AspectUpdates = new Dictionary<string, object>
            {
                ["title"] = title,
            },
        };
}

public sealed class LifecycleHookRecorder
{
    public List<RecordedLifecycleEvent> Events { get; } = [];

    public LifecyclePoint? RejectAt { get; set; }

    public LifecyclePoint? FailAt { get; set; }

    public LifecyclePoint? ThrowAt { get; set; }

    public LifecyclePoint? CancelAt { get; set; }

    public LifecycleHookOutcome RecordBefore(
        string hookName,
        ResourceLifecycleContext context,
        CancellationToken cancellationToken)
    {
        Record(hookName, context);

        if (CancelAt == context.LifecyclePoint)
            throw new OperationCanceledException(cancellationToken);

        if (ThrowAt == context.LifecyclePoint)
            throw new InvalidOperationException($"boom at {context.LifecyclePoint}");

        if (FailAt == context.LifecyclePoint)
            return LifecycleHookOutcome.Fail("test-failed", $"failed at {context.LifecyclePoint}");

        if (RejectAt == context.LifecyclePoint)
            return LifecycleHookOutcome.Reject("test-rejected", $"rejected at {context.LifecyclePoint}");

        return LifecycleHookOutcome.Continue();
    }

    public void RecordAfter(
        string hookName,
        ResourceLifecycleContext context,
        CancellationToken cancellationToken)
    {
        Record(hookName, context);

        if (CancelAt == context.LifecyclePoint)
            throw new OperationCanceledException(cancellationToken);

        if (ThrowAt == context.LifecyclePoint || FailAt == context.LifecyclePoint)
            throw new InvalidOperationException($"boom at {context.LifecyclePoint}");
    }

    private void Record(string hookName, ResourceLifecycleContext context) =>
        Events.Add(new RecordedLifecycleEvent(hookName, context.LifecyclePoint, context.OperationId, context));
}

public sealed record RecordedLifecycleEvent(
    string HookName,
    LifecyclePoint LifecyclePoint,
    Guid OperationId,
    ResourceLifecycleContext Context);

public abstract class RecordingResourceLifecycleHook(
    string hookName,
    LifecycleHookRecorder recorder) : ResourceLifecycleHook
{
    public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(recorder.RecordBefore(hookName, context, cancellationToken));

    public override ValueTask OnAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordAfter(hookName, context, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<LifecycleHookOutcome> OnBeforeActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(recorder.RecordBefore(hookName, context, cancellationToken));

    public override ValueTask OnAfterActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordAfter(hookName, context, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<LifecycleHookOutcome> OnBeforeDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(recorder.RecordBefore(hookName, context, cancellationToken));

    public override ValueTask OnAfterDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordAfter(hookName, context, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<LifecycleHookOutcome> OnBeforeExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(recorder.RecordBefore(hookName, context, cancellationToken));

    public override ValueTask OnAfterExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordAfter(hookName, context, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<LifecycleHookOutcome> OnBeforePreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(recorder.RecordBefore(hookName, context, cancellationToken));

    public override ValueTask OnAfterPreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordAfter(hookName, context, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<LifecycleHookOutcome> OnBeforeImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(recorder.RecordBefore(hookName, context, cancellationToken));

    public override ValueTask OnAfterImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        recorder.RecordAfter(hookName, context, cancellationToken);
        return ValueTask.CompletedTask;
    }
}

public sealed class FirstRecordingHook(LifecycleHookRecorder recorder)
    : RecordingResourceLifecycleHook("first", recorder);

public sealed class SecondRecordingHook(LifecycleHookRecorder recorder)
    : RecordingResourceLifecycleHook("second", recorder);
