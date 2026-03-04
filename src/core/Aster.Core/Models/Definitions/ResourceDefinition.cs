namespace Aster.Core.Models.Definitions;

/// <summary>
/// Metadata about a type of resource. Each call to <c>RegisterDefinitionAsync</c> produces a new
/// immutable version — existing versions are never mutated.
/// </summary>
/// <remarks>
/// Universal versioning pattern: <c>DefinitionId</c> is the stable logical identifier;
/// <c>Id</c> uniquely identifies this exact definition version.
/// </remarks>
public sealed record ResourceDefinition
{
    /// <summary>
    /// Logical persistent identifier (e.g., "Product"). Shared across all definition versions.
    /// </summary>
    public required string DefinitionId { get; init; }

    /// <summary>
    /// Version-specific unique identifier (GUID). Uniquely identifies this exact definition version.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Immutable version number, auto-incremented by the store on each <c>RegisterDefinitionAsync</c> call.
    /// Starts at 1. Composite key: <c>(DefinitionId, Version)</c>.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Aspect attachments. Key = <c>AspectDefinitionId</c> for unnamed attachments;
    /// <c>"{AspectDefinitionId}:{Name}"</c> composite for named attachments (e.g., <c>"Tag:Categories"</c>).
    /// </summary>
    public IReadOnlyDictionary<string, AspectDefinition> AspectDefinitions { get; init; }
        = new Dictionary<string, AspectDefinition>();

    /// <summary>
    /// If <see langword="true"/>, only one instance can exist for this definition.
    /// <c>CreateAsync</c> throws <see cref="Aster.Core.Exceptions.SingletonViolationException"/> if an instance already exists.
    /// </summary>
    public bool IsSingleton { get; init; }
}
