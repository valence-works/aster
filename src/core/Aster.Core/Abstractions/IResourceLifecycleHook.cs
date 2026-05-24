using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Abstractions;

/// <summary>
/// Host hook that can observe or gate resource lifecycle operations.
/// </summary>
public interface IResourceLifecycleHook
{
    /// <summary>
    /// Runs before a resource version is saved.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after a resource version is saved.
    /// </summary>
    ValueTask OnAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs before a resource version is activated.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after a resource version is activated.
    /// </summary>
    ValueTask OnAfterActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs before a resource version is deactivated.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after a resource version is deactivated.
    /// </summary>
    ValueTask OnAfterDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs before a portable snapshot is exported.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after a portable snapshot is exported.
    /// </summary>
    ValueTask OnAfterExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs before an import preview is planned.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforePreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after an import preview is planned.
    /// </summary>
    ValueTask OnAfterPreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs before a portable snapshot import is applied.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after a portable snapshot import has completed.
    /// </summary>
    ValueTask OnAfterImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Convenience base class for hooks that only need to override selected lifecycle points.
/// </summary>
public abstract class ResourceLifecycleHook : IResourceLifecycleHook
{
    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterExportAsync(
        ResourceExportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforePreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterPreviewImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
