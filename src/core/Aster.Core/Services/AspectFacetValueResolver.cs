using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        var property = FindProperty(aspectPayload, facetKey);
        if (property is null)
            return false;

        value = property.GetValue(aspectPayload);
        return value is not null;
    }

    /// <summary>
    /// For a POCO payload, the JSON name the stores write the facet's property under, when queries won't find it:
    /// they look up the facet ID and its camel-case form. Null for dictionaries, JSON, and matching names.
    /// </summary>
    /// <param name="aspectPayload">The aspect payload.</param>
    /// <param name="facetKey">The facet definition identifier.</param>
    public static string? StoredNameMismatch(object? aspectPayload, string facetKey)
    {
        if (aspectPayload is null or JsonElement || IsDictionary(aspectPayload) || FindProperty(aspectPayload, facetKey) is not { } property)
            return null;

        // The stores serialize with the web defaults: [JsonPropertyName] wins, otherwise camel case.
        var stored = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
            ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        return string.Equals(stored, facetKey, StringComparison.Ordinal)
            || string.Equals(stored, ToCamelCase(facetKey), StringComparison.Ordinal)
                ? null
                : stored;
    }

    // Facet IDs derived from typed aspects use [JsonPropertyName] when present, matched exactly as serialized;
    // otherwise fall back to the CLR property name, case-insensitively.
    // A [JsonPropertyName] equal to either name queries look for (the facet key or its camel form) wins over the
    // CLR property name.
    private static PropertyInfo? FindProperty(object aspectPayload, string facetKey)
    {
        var camel = ToCamelCase(facetKey);
        return aspectPayload.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(candidate =>
                    candidate.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name is { } name
                    && (string.Equals(name, facetKey, StringComparison.Ordinal) || string.Equals(name, camel, StringComparison.Ordinal)))
            ?? aspectPayload.GetType().GetProperty(facetKey, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
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
        // Numbers outside long/decimal range stay elements so they are rejected as type mismatches.
        JsonValueKind.Number => element,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        // Kept as an element so a present object is validated (and rejected) like any other value.
        JsonValueKind.Object => element,
        _ => null,
    };

    /// <summary>
    /// The camel-case form a facet ID is also looked up under: the first character lower-cased (invariant), the rest
    /// unchanged. Registration refuses two facets of one aspect with the same form (<c>Name</c> and <c>name</c>).
    /// </summary>
    /// <param name="value">The facet ID.</param>
    internal static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : string.Create(value.Length, value, static (chars, source) =>
            {
                chars[0] = char.ToLowerInvariant(source[0]);
                source.AsSpan(1).CopyTo(chars[1..]);
            });
}
