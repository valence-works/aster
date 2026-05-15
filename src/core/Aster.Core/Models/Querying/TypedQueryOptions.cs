namespace Aster.Core.Models.Querying;

/// <summary>
/// Overrides convention-based typed query mapping for a single helper chain.
/// </summary>
/// <param name="AspectKey">Optional aspect key override.</param>
/// <param name="FacetIdentifier">Optional facet identifier override.</param>
public sealed record TypedQueryOptions(string? AspectKey = null, string? FacetIdentifier = null);
