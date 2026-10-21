using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Abstractions;

/// <summary>
/// Coordinates deterministic lifecycle hook invocation.
/// </summary>
/// <remarks>
/// The default dispatcher resolves hooks from a fresh dependency-injection scope for each before or after dispatch call.
/// Hooks registered with <c>AddResourceLifecycleHook&lt;THook&gt;()</c> are singletons and preserve instance state across
/// paired before/after calls. Manually registered scoped or transient hooks MUST use <c>OperationId</c> or external state
/// for correlation rather than relying on the same hook instance being reused.
/// </remarks>
public interface IResourceLifecycleHookDispatcher
{
    /// <summary>
    /// Invokes before-save hooks.
    /// </summary>
    ValueTask InvokeBeforeSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes after-save hooks.
    /// </summary>
    ValueTask InvokeAfterSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes before-activation hooks.
    /// </summary>
    ValueTask InvokeBeforeActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes after-activation hooks.
    /// </summary>
    ValueTask InvokeAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes before-deactivation hooks.
    /// </summary>
    ValueTask InvokeBeforeDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes after-deactivation hooks.
    /// </summary>
    ValueTask InvokeAfterDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes before-export hooks.
    /// </summary>
    ValueTask InvokeBeforeExportAsync(ResourceExportLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes after-export hooks.
    /// </summary>
    ValueTask InvokeAfterExportAsync(ResourceExportLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes before-preview-import hooks.
    /// </summary>
    ValueTask InvokeBeforePreviewImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes after-preview-import hooks.
    /// </summary>
    ValueTask InvokeAfterPreviewImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes before-import hooks.
    /// </summary>
    ValueTask InvokeBeforeImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes after-import hooks.
    /// </summary>
    ValueTask InvokeAfterImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes before-archive hooks. The default invokes none, so a dispatcher written before the archive points
    /// existed keeps compiling; the dispatcher <c>AddAsterCore()</c> registers invokes them.
    /// </summary>
    ValueTask InvokeBeforeArchiveAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Invokes after-archive hooks. The default invokes none.
    /// </summary>
    ValueTask InvokeAfterArchiveAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Invokes before-restore hooks. The default invokes none.
    /// </summary>
    ValueTask InvokeBeforeRestoreAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Invokes after-restore hooks. The default invokes none.
    /// </summary>
    ValueTask InvokeAfterRestoreAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
