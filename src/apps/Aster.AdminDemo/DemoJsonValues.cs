using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Aster.AdminDemo;

internal static class DemoJsonValues
{
    public static object? ToPlain(object? value) => value switch
    {
        null => null,
        JsonElement element => ConvertJsonElement(element),
        IDictionary<string, object?> typed => typed.ToDictionary(static item => item.Key, static item => ToPlain(item.Value), StringComparer.Ordinal),
        IDictionary dictionary => dictionary.Cast<DictionaryEntry>()
            .Where(static entry => entry.Key is string)
            .ToDictionary(static entry => (string)entry.Key, static entry => ToPlain(entry.Value), StringComparer.Ordinal),
        IEnumerable<object?> items when value is not string => items.Select(ToPlain).ToList(),
        _ => value,
    };

    public static IReadOnlyDictionary<string, object?> ToFacetDictionary(object? value)
    {
        var plain = ToPlain(value);
        if (plain is IDictionary<string, object?> nullableDictionary)
            return new Dictionary<string, object?>(nullableDictionary, StringComparer.Ordinal);

        if (plain is IDictionary<string, object> dictionary)
            return dictionary.ToDictionary(static item => item.Key, static item => (object?)item.Value, StringComparer.Ordinal);

        if (plain is not null)
        {
            return plain.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(static property => property.Name, property => property.GetValue(plain), StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public static Dictionary<string, object> ToSdkAspects(Dictionary<string, Dictionary<string, object?>> aspects)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (aspectKey, facets) in aspects)
        {
            result[aspectKey] = facets.ToDictionary(
                static item => item.Key,
                static item => ToPlain(item.Value),
                StringComparer.Ordinal);
        }

        return result;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(static property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString(),
    };
}
