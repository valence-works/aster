using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aster.Elsa;

/// <summary>
/// Turns the aspects input of Create Resource and Update Resource into Aster aspect payloads: one JSON value per aspect
/// key, as <c>Aster.AspNetCore</c> passes them on. The input may be a JSON object as text, a <see cref="JsonElement"/>
/// or <see cref="JsonObject"/>, a dictionary (including the <see cref="System.Dynamic.ExpandoObject"/> Elsa's
/// JavaScript expressions produce), or any other object, which is serialized to JSON first.
/// </summary>
internal static class AsterAspectPayload
{
    // No naming policy: aspect keys and facet IDs are case-sensitive, so an object's member names are kept as written.
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static Dictionary<string, object> ToAspects(object? input)
    {
        var aspects = new Dictionary<string, object>(StringComparer.Ordinal);
        var element = ToElement(input);
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return aspects;
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid($"The aspects input must be a JSON object keyed by aspect key, not a JSON {element.ValueKind.ToString().ToLowerInvariant()}.");

        foreach (var property in element.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                throw Invalid("An aspect key in the aspects input is empty.");
            aspects[property.Name] = property.Value.Clone();
        }

        return aspects;
    }

    private static JsonElement ToElement(object? input)
    {
        try
        {
            switch (input)
            {
                case null:
                    return default;
                case JsonElement json:
                    return json;
                case string text when string.IsNullOrWhiteSpace(text):
                    return default;
                case string text:
                    return Parse(text);
                case JsonNode node:
                    return JsonSerializer.SerializeToElement(node, SerializerOptions);
                default:
                    return JsonSerializer.SerializeToElement(input, input.GetType(), SerializerOptions);
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw Invalid($"The aspects input is not valid JSON: {exception.Message}", exception);
        }
    }

    private static JsonElement Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static AsterActivityException Invalid(string message, Exception? inner = null) =>
        new(AsterActivityFaultCodes.AspectsInvalid, message, inner);
}
