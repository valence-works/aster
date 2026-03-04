using System.Text.Json;
using Aster.Core.Abstractions;

namespace Aster.Core.Services;

/// <summary>
/// <see cref="ITypedFacetBinder"/> implementation using <c>System.Text.Json</c>.
/// Applies State Replace semantics: the entire facet value is replaced on each write.
/// </summary>
public sealed class SystemTextJsonFacetBinder : ITypedFacetBinder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public object? Serialize<T>(T value)
    {
        if (value is null)
            return null;

        // Serialize to a JSON string for portable, round-trippable storage
        return JsonSerializer.Serialize(value, Options);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(object? raw)
    {
        if (raw is null)
            return default;

        // Fast path: already the correct type
        if (raw is T typed)
            return typed;

        // Deserialize from JSON string
        if (raw is string json)
            return JsonSerializer.Deserialize<T>(json, Options);

        // Deserialize from JsonElement (e.g., when raw came from a deserialized JSON doc)
        if (raw is JsonElement element)
            return JsonSerializer.Deserialize<T>(element.GetRawText(), Options);

        // Fallback: round-trip through JSON for type coercion
        var jsonStr = JsonSerializer.Serialize(raw, Options);
        return JsonSerializer.Deserialize<T>(jsonStr, Options);
    }
}
