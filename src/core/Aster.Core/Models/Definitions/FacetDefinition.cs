namespace Aster.Core.Models.Definitions;

/// <summary>
/// Defines a single typed sub-field ("field") within an Aspect (e.g., <c>Amount</c> inside <c>PriceAspect</c>).
/// Embedded within <see cref="AspectDefinition"/>; not stored independently.
/// Analogous to a Field on a Part in Orchard Core.
/// </summary>
public sealed record FacetDefinition
{
    /// <summary>
    /// Logical identifier for this field within its parent aspect (e.g., "Amount").
    /// Unique within the owning <see cref="AspectDefinition"/>.
    /// </summary>
    public required string FacetDefinitionId { get; init; }

    /// <summary>
    /// Data type descriptor ("string", "int", "decimal", "bool", "datetime").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the field must be present on save.
    /// </summary>
    public bool IsRequired { get; init; }
}
