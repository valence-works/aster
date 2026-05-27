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
    /// Optional bounded resource identifier set. When <see langword="null"/> or empty, all resources in scope are read.
    /// Matching is ordinal after provider normalization. If a non-empty collection contains only null,
    /// empty, or whitespace identifiers, providers must preserve bounded intent and return no resources.
    /// </summary>
    public IReadOnlyCollection<string>? ResourceIds { get; init; }

    /// <summary>
    /// Normalizes <see cref="ResourceIds"/> into an ordinal bounded-resource selection.
    /// </summary>
    /// <returns>The normalized bounded-resource selection.</returns>
    public ResourceVersionReadResourceIdSelection GetResourceIdSelection()
    {
        if (ResourceIds is not { Count: > 0 })
            return ResourceVersionReadResourceIdSelection.Unbounded;

        var values = ResourceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        return new ResourceVersionReadResourceIdSelection(IsBounded: true, values);
    }
}

/// <summary>
/// Normalized resource identifier selection for version reads.
/// </summary>
/// <param name="IsBounded">Whether the caller supplied an explicit non-empty resource identifier collection.</param>
/// <param name="Values">Ordinal resource identifiers after null, empty, and whitespace values are removed.</param>
public readonly record struct ResourceVersionReadResourceIdSelection(
    bool IsBounded,
    IReadOnlySet<string> Values)
{
    /// <summary>
    /// Unbounded selection that matches every resource in the requested tenant and version scope.
    /// </summary>
    public static ResourceVersionReadResourceIdSelection Unbounded { get; } =
        new(IsBounded: false, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Gets whether the caller supplied a bounded collection that normalized to no valid identifiers.
    /// </summary>
    public bool IsEmptyBound => IsBounded && Values.Count == 0;

    /// <summary>
    /// Returns whether the normalized selection includes the specified resource identifier.
    /// </summary>
    /// <param name="resourceId">The resource identifier to test.</param>
    /// <returns><see langword="true"/> when the selection is unbounded or contains the resource identifier.</returns>
    public bool Matches(string resourceId) => !IsBounded || Values.Contains(resourceId);
}
