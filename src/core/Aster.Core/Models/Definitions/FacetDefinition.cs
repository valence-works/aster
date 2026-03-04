namespace Aster.Core.Models.Definitions;

/// <summary>
/// Defines a single typed sub-field within an Aspect (e.g., <c>Amount</c> inside <c>PriceAspect</c>).
/// Embedded as a snapshot within <see cref="AspectDefinition"/>; not stored independently.
/// </summary>
public sealed record FacetDefinition
{
    /// <summary>
    /// Logical persistent identifier (e.g., "Amount"). Shared across all facet definition versions.
    /// </summary>
    public required string FacetDefinitionId { get; init; }

    /// <summary>
    /// Version-specific unique identifier (GUID). Uniquely identifies this exact facet definition version.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Immutable version number. Composite key: <c>(FacetDefinitionId, Version)</c>.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Data type descriptor ("string", "int", "decimal", "bool", "datetime").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the field must be present on save.
    /// </summary>
    public bool IsRequired { get; init; }
}
