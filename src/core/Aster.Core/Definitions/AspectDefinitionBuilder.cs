using Aster.Core.Models.Definitions;

namespace Aster.Core.Definitions;

/// <summary>
/// Builds standalone <see cref="AspectDefinition"/>s for registration in
/// <see cref="Abstractions.IAspectDefinitionStore"/> (ADR 0003 §5).
/// </summary>
public static class AspectDefinitionBuilder
{
    /// <summary>
    /// Builds a shared aspect definition from <typeparamref name="T"/>: the ID is <c>typeof(T).Name</c>, and
    /// the facets are derived exactly as for <see cref="ResourceDefinitionBuilder.WithAspect{T}()"/>.
    /// Attach the registered aspect with <see cref="ResourceDefinitionBuilder.WithSharedAspect{T}(int?, string?)"/>.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <param name="requiresName">Whether every attachment of the aspect must be named.</param>
    /// <returns>An aspect definition; the store assigns its <c>Version</c> on registration.</returns>
    public static AspectDefinition FromType<T>(bool requiresName = false) => new()
    {
        AspectDefinitionId = typeof(T).Name,
        Id = Guid.NewGuid().ToString(),
        Version = 0,
        RequiresName = requiresName,
        FacetDefinitions = TypedAspectFacets.Derive(typeof(T)),
    };
}
