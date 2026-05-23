using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Abstractions;

/// <summary>
/// Coordinates deterministic lifecycle hook invocation.
/// </summary>
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
}
