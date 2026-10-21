using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Abstractions;

/// <summary>
/// Host hook that can observe or gate resource lifecycle operations.
/// </summary>
/// <remarks>
/// Before and after calls are paired: once every before hook for an operation continues, the after hooks run
/// whether the underlying operation then succeeds or fails. An after hook reads
/// <see cref="ResourceLifecycleContext.OperationStatus"/> to tell the two apart, and
/// <see cref="ResourceLifecycleContext.OperationFailure"/> for the reason of a failure. Hooks that should only
/// observe completed changes, such as audit or notification hooks, return early unless the status is
/// <see cref="LifecycleOperationStatus.Succeeded"/>.
/// </remarks>
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

    /// <summary>
    /// Runs before an archive or soft-delete lifecycle marker is written for a resource. The default continues.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeArchiveAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <summary>
    /// Runs after an archive or soft-delete lifecycle marker is written for a resource. The default does nothing.
    /// </summary>
    ValueTask OnAfterArchiveAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Runs before a resource's archive or soft-delete lifecycle marker is cleared. The default continues.
    /// </summary>
    ValueTask<LifecycleHookOutcome> OnBeforeRestoreAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <summary>
    /// Runs after a resource's archive or soft-delete lifecycle marker is cleared. The default does nothing.
    /// </summary>
    ValueTask OnAfterRestoreAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
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

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeArchiveAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterArchiveAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask<LifecycleHookOutcome> OnBeforeRestoreAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(LifecycleHookOutcome.Continue());

    /// <inheritdoc />
    public virtual ValueTask OnAfterRestoreAsync(
        ResourceArchiveLifecycleContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
