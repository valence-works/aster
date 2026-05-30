using Aster.Core.Exceptions;
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
    async ValueTask<ResourceVersionHistoryBatchResult> GetHistoriesAsync(
        ResourceVersionHistoryBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ResourceIds);

        var tenant = TenantScope.Resolve(request.TenantScope);
        if (string.IsNullOrWhiteSpace(tenant.TenantId))
        {
            throw new TenantScopeException(
                TenantScopeException.InvalidCode,
                "Tenant scope must include a non-empty tenant identifier.");
        }

        var resourceIds = new List<string>(request.ResourceIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resourceId in request.ResourceIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

            if (seen.Add(resourceId))
                resourceIds.Add(resourceId);
        }

        var histories = new List<ResourceVersionHistoryResult>(resourceIds.Count);
        foreach (var resourceId in resourceIds)
        {
            histories.Add(await GetHistoryAsync(new ResourceVersionHistoryRequest
            {
                TenantScope = request.TenantScope,
                ResourceId = resourceId,
            }, cancellationToken));
        }

        return new ResourceVersionHistoryBatchResult
        {
            TenantScope = tenant,
            Histories = histories,
        };
    }
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
