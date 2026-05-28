using Aster.Core.Models.Instances;

namespace Aster.Core.Abstractions;

/// <summary>
/// Host-facing lifecycle restore workflow service.
/// </summary>
public interface IResourceLifecycleRestoreService
{
    /// <summary>
    /// Previews lifecycle marker restoration without mutating marker state.
    /// </summary>
    ValueTask<ResourceLifecycleRestorePreviewResult> PreviewRestoreAsync(
        ResourceLifecycleRestoreRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores selected archive or soft-delete markers by clearing matching marker state.
    /// </summary>
    ValueTask<ResourceLifecycleRestoreApplicationResult> RestoreAsync(
        ResourceLifecycleRestoreRequest request,
        CancellationToken cancellationToken = default);
}
