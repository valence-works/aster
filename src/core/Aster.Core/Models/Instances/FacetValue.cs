namespace Aster.Core.Models.Instances;

/// <summary>
/// A single resolved primitive value for one facet; used in Query AST <c>FacetValue</c> filter expressions.
/// </summary>
public sealed record FacetValue
{
    /// <summary>
    /// FK to <c>FacetDefinition.FacetDefinitionId</c> (logical identifier).
    /// </summary>
    public required string FacetDefinitionId { get; init; }

    /// <summary>
    /// Raw value (string, int, bool, decimal, DateTime).
    /// </summary>
    public required object Value { get; init; }
}
