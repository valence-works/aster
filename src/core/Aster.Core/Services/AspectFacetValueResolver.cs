using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Aster.Core.Services;

/// <summary>
/// Resolves a facet value from an aspect payload, supporting the three established payload
/// shapes — <see cref="IDictionary"/> entries, <see cref="JsonElement"/> properties, and
/// POCO properties (case-insensitive) — mirroring <c>IndexProjectionEvaluator.TryResolveFacetValue</c>
/// semantics, including <see cref="JsonElement"/> → CLR conversion.
/// </summary>
internal static class AspectFacetValueResolver
{
    /// <summary>
    /// Attempts to resolve a facet value from an aspect payload.
    /// Returns <see langword="false"/> when the value is absent or null.
    /// </summary>
    /// <param name="aspectPayload">The aspect payload (dictionary, <see cref="JsonElement"/>, or POCO).</param>
    /// <param name="facetKey">The facet definition identifier used as the property/entry key.</param>
    /// <param name="value">The resolved value, when present and non-null.</param>
    public static bool TryResolveValue(object? aspectPayload, string facetKey, out object? value)
    {
        value = null;

        if (aspectPayload is null || string.IsNullOrWhiteSpace(facetKey))
            return false;

        if (IsDictionary(aspectPayload))
            return TryGetDictionaryValue(aspectPayload, facetKey, out value) && value is not null;

        if (aspectPayload is JsonElement element)
            return TryGetJsonElementValue(element, facetKey, out value) && value is not null;

        var property = aspectPayload.GetType().GetProperty(
            facetKey,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property is null)
            return false;

        value = property.GetValue(aspectPayload);
        return value is not null;
    }

    private static bool IsDictionary(object value) =>
        value is IReadOnlyDictionary<string, object>
            or IDictionary<string, object>
            or IDictionary;

    private static bool TryGetDictionaryValue(object value, string key, out object? result)
    {
        switch (value)
        {
            case IReadOnlyDictionary<string, object> readOnlyDictionary:
                return readOnlyDictionary.TryGetValue(key, out result);
            case IDictionary<string, object> dictionary:
                return dictionary.TryGetValue(key, out result);
            case IDictionary dictionary:
                if (dictionary.Contains(key))
                {
                    result = dictionary[key];
                    return true;
                }

                break;
        }

        result = null;
        return false;
    }

    private static bool TryGetJsonElementValue(JsonElement element, string key, out object? value)
    {
        if (element.ValueKind != JsonValueKind.Object
            || (!element.TryGetProperty(key, out var property)
                && !element.TryGetProperty(ToCamelCase(key), out property)))
        {
            value = null;
            return false;
        }

        value = ConvertJsonElement(property);
        return value is not null;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        _ => null,
    };

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : string.Create(value.Length, value, static (chars, source) =>
            {
                chars[0] = char.ToLowerInvariant(source[0]);
                source.AsSpan(1).CopyTo(chars[1..]);
            });
}
