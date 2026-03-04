namespace Aster.Core.Models.Querying;

/// <summary>
/// Base type for all filter expressions in the <see cref="ResourceQuery"/> AST.
/// </summary>
public abstract record FilterExpression;

/// <summary>
/// Filters resources by a metadata field (e.g., <c>ResourceId</c>, <c>DefinitionId</c>, <c>Owner</c>).
/// </summary>
/// <param name="Field">The metadata field name (case-insensitive).</param>
/// <param name="Value">The value to compare against.</param>
/// <param name="Operator">The comparison operator to apply.</param>
public sealed record MetadataFilter(string Field, string Value, ComparisonOperator Operator) : FilterExpression;

/// <summary>
/// Filters resources that have an aspect at the specified key (presence check only).
/// </summary>
/// <param name="AspectKey">
/// The aspect key to check for (<c>AspectDefinitionId</c> or <c>"{AspectDefinitionId}:{Name}"</c>).
/// </param>
public sealed record AspectPresenceFilter(string AspectKey) : FilterExpression;

/// <summary>
/// Filters resources by a specific facet value within an aspect.
/// </summary>
/// <param name="AspectKey">The aspect key containing the facet.</param>
/// <param name="FacetDefinitionId">The logical facet identifier.</param>
/// <param name="Value">The value to compare against.</param>
/// <param name="Operator">The comparison operator to apply.</param>
public sealed record FacetValueFilter(string AspectKey, string FacetDefinitionId, object Value, ComparisonOperator Operator) : FilterExpression;
