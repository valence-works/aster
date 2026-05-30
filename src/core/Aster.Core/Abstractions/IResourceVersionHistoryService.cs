using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Host-facing service for read-only resource version history inspection.
/// </summary>
public interface IResourceVersionHistoryService
{
    /// <summary>
    /// Reads the ordered version history for one resource in one effective tenant.
    /// </summary>
    ValueTask<ResourceVersionHistoryResult> GetHistoryAsync(
        ResourceVersionHistoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads ordered version histories for an explicit set of resources in one effective tenant.
    /// </summary>
    ValueTask<ResourceVersionHistoryBatchResult> GetHistoriesAsync(
        ResourceVersionHistoryBatchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-facing reader for resource activation states.
/// </summary>
public interface IResourceActivationStateReader
{
    /// <summary>
    /// Reads activation states for the supplied resource identifiers in one tenant.
    /// </summary>
    ValueTask<IReadOnlyList<ActivationState>> ReadActivationStatesAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
