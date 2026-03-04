using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;

namespace Aster.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="AspectInstance"/> that provide typed facet access via <see cref="ITypedFacetBinder"/>.
/// </summary>
public static class AspectInstanceExtensions
{
    /// <summary>
    /// Deserializes the facet value stored under <paramref name="facetDefinitionId"/> to the specified POCO type.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the facet value.</typeparam>
    /// <param name="instance">The aspect instance to read from.</param>
    /// <param name="facetDefinitionId">The logical facet identifier.</param>
    /// <param name="binder">The binder to use for deserialization.</param>
    /// <returns>The deserialized POCO, or <see langword="default"/> if the key is absent.</returns>
    public static T? GetFacet<T>(this AspectInstance instance, string facetDefinitionId, ITypedFacetBinder binder)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentException.ThrowIfNullOrWhiteSpace(facetDefinitionId);

        return instance.Facets.TryGetValue(facetDefinitionId, out var raw)
            ? binder.Deserialize<T>(raw)
            : default;
    }

    /// <summary>
    /// Returns a new <see cref="AspectInstance"/> with the facet at <paramref name="facetDefinitionId"/>
    /// replaced by the serialized form of <paramref name="value"/> (State Replace semantics).
    /// </summary>
    /// <typeparam name="T">The POCO type representing the facet value.</typeparam>
    /// <param name="instance">The source aspect instance (unmodified).</param>
    /// <param name="facetDefinitionId">The logical facet identifier.</param>
    /// <param name="value">The POCO value to set.</param>
    /// <param name="binder">The binder to use for serialization.</param>
    /// <returns>A new <see cref="AspectInstance"/> record with the updated facet entry.</returns>
    public static AspectInstance SetFacet<T>(this AspectInstance instance, string facetDefinitionId, T value, ITypedFacetBinder binder)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentException.ThrowIfNullOrWhiteSpace(facetDefinitionId);

        var serialized = binder.Serialize(value)
            ?? throw new InvalidOperationException($"Binder returned null when serializing facet '{facetDefinitionId}'.");

        var updated = new Dictionary<string, object>(instance.Facets, StringComparer.Ordinal)
        {
            [facetDefinitionId] = serialized
        };

        return instance with { Facets = updated };
    }
}
