namespace Aster.Core.Models.Instances;

/// <summary>
/// The typed view of one aspect attachment on a <see cref="Resource"/> version.
/// </summary>
/// <remarks>
/// Unnamed attachment key = <c>AspectDefinitionId</c>.
/// Named attachment key = <c>"{AspectDefinitionId}:{Name}"</c> composite.
/// </remarks>
public sealed record AspectInstance
{
    /// <summary>
    /// FK to <c>AspectDefinition.AspectDefinitionId</c> (logical identifier).
    /// </summary>
    public required string AspectDefinitionId { get; init; }

    /// <summary>
    /// Discriminator for named attachments; <see langword="null"/> for unnamed attachments.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Facet values keyed by <c>FacetDefinition.FacetDefinitionId</c>.
    /// </summary>
    public IReadOnlyDictionary<string, object> Facets { get; init; }
        = new Dictionary<string, object>();
}
