using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;

namespace Aster.Core.Definitions;

/// <summary>
/// Fluent builder for constructing a <see cref="ResourceDefinition"/>.
/// </summary>
/// <remarks>
/// Aspect key scheme:
/// <list type="bullet">
///   <item>Unnamed: <c>AspectDefinitionId</c> (derived from <c>typeof(T).Name</c>)</item>
///   <item>Named: <c>"{AspectDefinitionId}:{Name}"</c> composite</item>
/// </list>
/// Duplicate keys are detected at <see cref="Build"/> time and throw <see cref="DuplicateAspectAttachmentException"/>.
/// </remarks>
public sealed class ResourceDefinitionBuilder
{
    private string? definitionId;
    private bool isSingleton;
    private readonly List<(string Key, AspectDefinition Definition)> aspectEntries = [];

    /// <summary>
    /// Sets the logical persistent definition identifier.
    /// </summary>
    /// <param name="id">The definition ID (e.g., "Product").</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithDefinitionId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        definitionId = id;
        return this;
    }

    /// <summary>
    /// Specifies whether only one instance can exist for this definition.
    /// </summary>
    /// <param name="singleton"><see langword="true"/> to enforce singleton behaviour.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithSingleton(bool singleton = true)
    {
        isSingleton = singleton;
        return this;
    }

    /// <summary>
    /// Attaches an unnamed aspect derived from the type <typeparamref name="T"/>.
    /// The <c>AspectDefinitionId</c> is set to <c>typeof(T).Name</c>.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithAspect<T>()
    {
        var aspectId = typeof(T).Name;
        var aspect = new AspectDefinition
        {
            AspectDefinitionId = aspectId,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            RequiresName = false,
        };
        aspectEntries.Add((aspectId, aspect));
        return this;
    }

    /// <summary>
    /// Attaches a named aspect derived from the type <typeparamref name="T"/>.
    /// The attachment key is <c>"{typeof(T).Name}:{name}"</c>.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <param name="name">The discriminator name for this attachment.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithNamedAspect<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var aspectId = typeof(T).Name;
        var key = $"{aspectId}:{name}";
        var aspect = new AspectDefinition
        {
            AspectDefinitionId = aspectId,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            RequiresName = true,
        };
        aspectEntries.Add((key, aspect));
        return this;
    }

    /// <summary>
    /// Attaches an unnamed named aspect derived from the type <typeparamref name="T"/>,
    /// recording the CLR type for typed binding.
    /// Equivalent to <see cref="WithAspect{T}()"/> with additional type registration intent.
    /// The <c>AspectDefinitionId</c> is set to <c>typeof(T).Name</c>.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithTypedAspect<T>() => WithAspect<T>();

    /// <summary>
    /// Adds a typed <see cref="FacetDefinition"/> to the most recently registered aspect,
    /// using <c>typeof(T).Name</c> as the <c>FacetDefinitionId</c>.
    /// </summary>
    /// <typeparam name="T">The CLR type representing the facet value (e.g., <c>decimal</c>, <c>string</c>).</typeparam>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no aspect has been registered yet.</exception>
    public ResourceDefinitionBuilder WithTypedFacet<T>()
    {
        if (aspectEntries.Count == 0)
            throw new InvalidOperationException("Register an aspect (WithAspect or WithTypedAspect) before calling WithTypedFacet.");

        var facetDefinitionId = typeof(T).Name;
        var facet = new FacetDefinition
        {
            FacetDefinitionId = facetDefinitionId,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            Type = facetDefinitionId.ToLowerInvariant(),
            IsRequired = false,
        };

        var (key, lastAspect) = aspectEntries[^1];
        var updatedFacets = new List<FacetDefinition>(lastAspect.FacetDefinitions) { facet };
        aspectEntries[^1] = (key, lastAspect with { FacetDefinitions = updatedFacets });
        return this;
    }

    /// <summary>
    /// Builds the <see cref="ResourceDefinition"/> from the configured state.
    /// </summary>
    /// <returns>The constructed definition. <c>Version</c> is set to 0 and auto-incremented by the store on registration.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <c>DefinitionId</c> has not been set.</exception>
    /// <exception cref="DuplicateAspectAttachmentException">
    /// Thrown if the same aspect key is attached more than once.
    /// </exception>
    public ResourceDefinition Build()
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new InvalidOperationException("DefinitionId must be set before calling Build().");

        // Validate uniqueness — detect first duplicate key
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in aspectEntries)
        {
            if (!seen.Add(key))
                throw new DuplicateAspectAttachmentException(key);
        }

        var aspectDict = aspectEntries.ToDictionary(e => e.Key, e => e.Definition);

        return new ResourceDefinition
        {
            DefinitionId = definitionId,
            Id = Guid.NewGuid().ToString(),
            Version = 0, // auto-incremented by the store
            AspectDefinitions = aspectDict,
            IsSingleton = isSingleton,
        };
    }
}
