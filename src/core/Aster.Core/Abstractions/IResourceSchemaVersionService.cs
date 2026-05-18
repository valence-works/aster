using Aster.Core.Models.Instances;

namespace Aster.Core.Abstractions;

/// <summary>
/// Provides explicit schema-version status and upgrade operations for resource versions.
/// </summary>
public interface IResourceSchemaVersionService
{
    /// <summary>
    /// Gets schema status for a single resource version snapshot.
    /// </summary>
    /// <param name="resource">The resource version snapshot to inspect.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The schema status for the supplied resource version.</returns>
    ValueTask<ResourceSchemaStatusResult> GetSchemaStatusAsync(
        Resource resource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly upgrades the latest resource version to a target definition version.
    /// </summary>
    /// <param name="resourceId">The logical resource identifier.</param>
    /// <param name="request">The schema upgrade request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The schema upgrade result.</returns>
    ValueTask<ResourceSchemaUpgradeResult> UpgradeAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken = default);
}
