using Aster.Core.Models.Tenancy;
using Aster.Core.Models.Instances;

namespace Aster.Core.Models.Querying;

/// <summary>
/// A portable query over the resource store. Translated to LINQ by <c>InMemoryQueryService</c>.
/// </summary>
public sealed record ResourceQuery
{
    /// <summary>
    /// Tenant scope for the query. When omitted, the default single-tenant scope is used.
    /// </summary>
    public TenantScope? TenantScope { get; init; }

    /// <summary>
    /// Which resource versions to query. Defaults to latest versions only.
    /// </summary>
    public ResourceVersionScope Scope { get; init; } = ResourceVersionScope.Latest;

    /// <summary>
    /// Channel used when <see cref="Scope"/> is <see cref="ResourceVersionScope.Active"/>.
    /// </summary>
    public string? ActivationChannel { get; init; }

    /// <summary>
    /// Optional — restricts results to resources with the given <c>DefinitionId</c>.
    /// Equivalent to a <see cref="MetadataFilter"/> on the <c>DefinitionId</c> field with <see cref="ComparisonOperator.Equals"/>.
    /// </summary>
    public string? DefinitionId { get; init; }

    /// <summary>
    /// Optional explicit lifecycle marker state filter. When omitted, lifecycle markers do not affect query results.
    /// </summary>
    public ResourceLifecycleMarkerState? LifecycleState { get; init; }

    /// <summary>
    /// Optional filter expression tree evaluated against each resource version.
    /// Supports metadata, aspect-presence, facet-value, and logical predicates.
    /// </summary>
    public FilterExpression? Filter { get; init; }

    /// <summary>
    /// Optional result ordering. Sorts are applied in sequence.
    /// </summary>
    public IReadOnlyList<SortExpression> Sorts { get; init; } = [];

    /// <summary>Number of results to skip (pagination). <see langword="null"/> = no skip.</summary>
    public int? Skip { get; init; }

    /// <summary>Maximum number of results to return. <see langword="null"/> = no limit.</summary>
    public int? Take { get; init; }
}
