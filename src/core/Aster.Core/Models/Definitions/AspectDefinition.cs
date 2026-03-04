namespace Aster.Core.Models.Definitions;

/// <summary>
/// Defines a reusable piece of data structure (e.g., "PriceAspect").
/// Embedded as a snapshot within <see cref="ResourceDefinition"/>; not stored independently.
/// </summary>
public sealed record AspectDefinition
{
    /// <summary>
    /// Logical persistent identifier (e.g., "Price"). Shared across all aspect definition versions.
    /// </summary>
    public required string AspectDefinitionId { get; init; }

    /// <summary>
    /// Version-specific unique identifier (GUID). Uniquely identifies this exact aspect definition version.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Immutable version number. Composite key: <c>(AspectDefinitionId, Version)</c>.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// If <see langword="true"/>, attachment must be named (e.g., "ListingPrice", "SalePrice").
    /// </summary>
    public bool RequiresName { get; init; }

    /// <summary>
    /// JSON Schema or type descriptor (reserved; <see langword="null"/> for Phase 1).
    /// </summary>
    public string? Schema { get; init; }

    /// <summary>
    /// Typed sub-fields within this aspect.
    /// </summary>
    public IReadOnlyList<FacetDefinition> FacetDefinitions { get; init; } = [];
}
