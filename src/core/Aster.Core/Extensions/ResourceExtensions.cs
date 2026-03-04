using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;

namespace Aster.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="Resource"/> that provide typed aspect access via <see cref="ITypedAspectBinder"/>.
/// </summary>
public static class ResourceExtensions
{
    /// <summary>
    /// Deserializes the aspect value stored under <paramref name="key"/> to the specified POCO type.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <param name="resource">The resource to read from.</param>
    /// <param name="key">The aspect key (<c>AspectDefinitionId</c> or <c>"{AspectDefinitionId}:{Name}"</c>).</param>
    /// <param name="binder">The binder to use for deserialization.</param>
    /// <returns>The deserialized POCO, or <see langword="default"/> if the key is absent.</returns>
    public static T? GetAspect<T>(this Resource resource, string key, ITypedAspectBinder binder)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return resource.Aspects.TryGetValue(key, out var raw)
            ? binder.Deserialize<T>(raw)
            : default;
    }

    /// <summary>
    /// Returns a new <see cref="Resource"/> with the aspect at <paramref name="key"/> replaced by
    /// the serialized form of <paramref name="value"/> (State Replace semantics).
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <param name="resource">The source resource (unmodified).</param>
    /// <param name="key">The aspect key.</param>
    /// <param name="value">The POCO value to set.</param>
    /// <param name="binder">The binder to use for serialization.</param>
    /// <returns>A new <see cref="Resource"/> record with the updated aspect entry.</returns>
    public static Resource SetAspect<T>(this Resource resource, string key, T value, ITypedAspectBinder binder)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var serialized = binder.Serialize(value)
            ?? throw new InvalidOperationException($"Binder returned null when serializing aspect '{key}'.");

        var updated = new Dictionary<string, object>(resource.Aspects, StringComparer.Ordinal)
        {
            [key] = serialized
        };

        return resource with { Aspects = updated };
    }
}
