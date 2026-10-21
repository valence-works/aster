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
    /// Reports what <see cref="UpgradeAsync"/> would do with the same request now, without writing: the target
    /// definition version, the aspect keys it would carry forward, and the diagnostics that would reject it.
    /// </summary>
    /// <param name="resourceId">The logical resource identifier.</param>
    /// <param name="request">The schema upgrade request, including its base version and aspect updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The schema upgrade preview.</returns>
    ValueTask<ResourceSchemaUpgradePreview> UpgradePreviewAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly upgrades the latest resource version to a target definition version. The upgraded payload is
    /// validated against the target definition's facets before it is written.
    /// </summary>
    /// <remarks>
    /// A request that cannot be applied returns <see cref="ResourceSchemaUpgradeStatus.Rejected"/> with
    /// <see cref="ResourceSchemaUpgradeResult.Diagnostics"/> (a missing resource, a stale base version, an invalid
    /// target, or an invalid upgraded payload) and writes nothing. Exceptions are left for programming errors
    /// (null or blank arguments, an invalid tenant scope), lifecycle hook failures and store failures.
    /// </remarks>
    /// <param name="resourceId">The logical resource identifier.</param>
    /// <param name="request">The schema upgrade request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The schema upgrade result.</returns>
    ValueTask<ResourceSchemaUpgradeResult> UpgradeAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken = default);
}
