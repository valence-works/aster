using Aster.Core.Abstractions;
using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Services;

internal sealed class NoopResourceLifecycleHookDispatcher : IResourceLifecycleHookDispatcher
{
    public static NoopResourceLifecycleHookDispatcher Instance { get; } = new();

    public ValueTask InvokeBeforeSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeAfterSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeBeforeActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeBeforeDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeAfterDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeBeforeExportAsync(ResourceExportLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeAfterExportAsync(ResourceExportLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeBeforePreviewImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeAfterPreviewImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeBeforeImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvokeAfterImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
