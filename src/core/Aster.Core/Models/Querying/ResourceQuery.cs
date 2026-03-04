namespace Aster.Core.Models.Querying;

/// <summary>
/// A portable query over the resource store. Translated to LINQ by <c>InMemoryQueryService</c>.
/// </summary>
public sealed record ResourceQuery
{
    /// <summary>
    /// Optional — restricts results to resources with the given <c>DefinitionId</c>.
    /// Equivalent to a <see cref="MetadataFilter"/> on the <c>DefinitionId</c> field with <see cref="ComparisonOperator.Equals"/>.
    /// </summary>
    public string? DefinitionId { get; init; }

    /// <summary>
    /// Optional filter expression tree evaluated against each resource version.
    /// Supports <see cref="ComparisonOperator.Equals"/> and <see cref="ComparisonOperator.Contains"/>.
    /// <see cref="ComparisonOperator.Range"/> throws <see cref="NotSupportedException"/> in Phase 1.
    /// </summary>
    public FilterExpression? Filter { get; init; }

    /// <summary>Number of results to skip (pagination). <see langword="null"/> = no skip.</summary>
    public int? Skip { get; init; }

    /// <summary>Maximum number of results to return. <see langword="null"/> = no limit.</summary>
    public int? Take { get; init; }
}
