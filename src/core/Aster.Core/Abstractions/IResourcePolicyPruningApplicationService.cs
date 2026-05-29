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
    /// Removes one resource version in the supplied tenant.
    /// </summary>
    /// <returns><see langword="true" /> when a matching version existed and was removed; otherwise <see langword="false" />.</returns>
    ValueTask<bool> PruneVersionAsync(
        string resourceId,
        int resourceVersion,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
