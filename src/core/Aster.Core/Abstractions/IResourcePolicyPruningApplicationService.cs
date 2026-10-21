using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Host-facing service for applying selected version-pruning preview outcomes.
/// </summary>
public interface IResourcePolicyPruningApplicationService
{
    /// <summary>
    /// Applies selected version-pruning candidates after current-state safety preflight.
    /// </summary>
    ValueTask<ResourcePolicyPruningApplicationResult> ApplyAsync(
        ResourcePolicyPruningApplicationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-facing storage capability for destructive resource version pruning.
/// </summary>
public interface IResourceVersionPruningStore
{
    /// <summary>
    /// Removes one resource version in the supplied tenant. The latest version, an active version, and a version
    /// whose removal would break <paramref name="floor"/> are refused, checked atomically with the delete.
    /// </summary>
    /// <param name="resourceId">The resource whose version is removed.</param>
    /// <param name="resourceVersion">The version to remove.</param>
    /// <param name="tenantScope">The tenant the resource belongs to.</param>
    /// <param name="floor">The versions that must remain afterwards, or <see langword="null" /> for no floor.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Exceptions.ResourceVersionPruneFloorException">Removing the version would break <paramref name="floor"/>.</exception>
    /// <returns><see langword="true" /> when a matching version existed and was removed; otherwise <see langword="false" />.</returns>
    ValueTask<bool> PruneVersionAsync(
        string resourceId,
        int resourceVersion,
        TenantScope tenantScope,
        ResourceVersionPruneFloor? floor = null,
        CancellationToken cancellationToken = default);
}
