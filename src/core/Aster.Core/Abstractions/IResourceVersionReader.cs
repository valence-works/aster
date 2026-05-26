using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Provider-agnostic reader for resource version snapshots.
/// Query providers use this contract to choose the candidate version set before applying filters.
/// </summary>
public interface IResourceVersionReader
{
    /// <summary>
    /// Reads resource versions using the requested scope.
    /// </summary>
    /// <param name="request">The version read request describing which versions should be returned.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching candidate resource versions.</returns>
    ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
        ResourceVersionReadRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request DTO for selecting a resource version set before query filtering.
/// </summary>
public sealed record ResourceVersionReadRequest
{
    /// <summary>
    /// Tenant scope for the read. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; init; }

    /// <summary>
    /// Which resource versions to include. Defaults to latest versions only.
    /// </summary>
    public ResourceVersionScope Scope { get; init; } = ResourceVersionScope.Latest;

    /// <summary>
    /// Required when <see cref="Scope"/> is <see cref="ResourceVersionScope.Active"/>.
    /// </summary>
    public string? ActivationChannel { get; init; }

    /// <summary>
    /// Optional bounded resource identifier set. When empty, all resources in scope are read.
    /// </summary>
    public HashSet<string> ResourceIds { get; init; } = new(StringComparer.Ordinal);
}
