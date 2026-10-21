using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Elsa.Api.Client.Resources.Scripting.Models;
using Elsa.Api.Client.Shared.Models;

namespace Aster.Elsa.Studio.Forms;

/// <summary>
/// Converts facet values between their JSON form in a resource payload and the values Elsa Studio's input editors
/// read and write. Editors that work on literal text return strings, the checkbox a <see cref="bool"/>, list editors a
/// list of strings, and the JSON and code editors a <see cref="WrappedInput"/> holding a literal expression; this
/// class turns each back into JSON of the facet's type, or into an input error the form shows on the field.
/// </summary>
internal static partial class AsterFieldValues
{
    private const string DateTimeEditorFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Returns the value to hand to the editor for a payload value.</summary>
    public static object? ToEditorValue(AsterFormField field, AsterFieldEditor editor, JsonNode? value)
    {
        switch (editor.Shape)
        {
            case AsterEditorValueShape.Boolean:
                return value is JsonValue boolean && boolean.TryGetValue<bool>(out var isChecked) && isChecked;

            case AsterEditorValueShape.DateTime:
                // Unspecified kind, so the picker shows the stored UTC value without shifting it to local time.
                return value is null ? null : TryParseInstant(ToText(value), out var instant) ? instant.ToString(DateTimeEditorFormat, CultureInfo.InvariantCulture) : ToText(value);

            case AsterEditorValueShape.List:
                return value is JsonArray array ? array.Select(ToText).ToList() : new List<string>();

            case AsterEditorValueShape.Json:
                return value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";

            case AsterEditorValueShape.Reference:
                return field.IsArray
                    ? value is JsonArray references ? references.Select(ToText).ToList() : new List<string>()
                    : value is null ? null : ToText(value);

            default:
                return value is null ? null : ToText(value);
        }
    }

    /// <summary>
    /// Converts what an editor wrote back into the payload value of <paramref name="field"/>. Returns
    /// <see langword="false"/> with an <paramref name="error"/> when the input doesn't fit the facet type.
    /// </summary>
    public static bool TryFromEditorValue(AsterFormField field, AsterFieldEditor editor, object? input, out JsonNode? value, out string? error)
    {
        value = null;
        error = null;
        input = Unwrap(input);

        if (editor.Shape == AsterEditorValueShape.Json || (field.Kind is AsterValueKind.Object or AsterValueKind.Any))
            return TryParseJson(input as string ?? input?.ToString(), out value, out error);

        if (field.IsArray)
        {
            var items = ToItems(input);
            if (items.Count == 0)
                return true;

            var array = new JsonArray();
            for (var index = 0; index < items.Count; index++)
            {
                if (!TryConvertScalar(field.Kind, items[index], field.UiHint, out var element, out var elementError))
                {
                    error = $"Item {index + 1}: {elementError}";
                    return false;
                }

                if (element is not null)
                    array.Add(element);
            }

            value = array.Count == 0 ? null : array;
            return true;
        }

        if (input is bool flag)
        {
            value = field.Kind == AsterValueKind.Boolean ? JsonValue.Create(flag) : JsonValue.Create(flag.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        return TryConvertScalar(field.Kind, input?.ToString(), field.UiHint, out value, out error);
    }

    /// <summary>Returns a scalar JSON value as editor text: strings unquoted, anything else as JSON.</summary>
    public static string ToText(JsonNode? value) => value switch
    {
        null => "",
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
        JsonValue scalar when scalar.GetValueKind() == JsonValueKind.True => "True",
        JsonValue scalar when scalar.GetValueKind() == JsonValueKind.False => "False",
        _ => value.ToJsonString(),
    };

    private static object? Unwrap(object? input) => input switch
    {
        WrappedInput { Expression: { } expression } => expression.Value,
        Expression expression => expression.Value,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => input,
    };

    private static List<string?> ToItems(object? input) => input switch
    {
        null => [],
        string text when string.IsNullOrWhiteSpace(text) => [],

        // A list editor may hand back JSON text (a JSON array) rather than a list.
        string text when text.TrimStart().StartsWith('[') => TryParseJson(text, out var parsed, out _) && parsed is JsonArray array
            ? array.Select(item => item is null ? null : ToText(item)).ToList()
            : [text],
        string text => [text],
        IEnumerable sequence => sequence.Cast<object?>().Select(static item => Unwrap(item)?.ToString()).ToList(),
        _ => [input.ToString()],
    };

    private static bool TryConvertScalar(AsterValueKind kind, string? text, string? uiHint, out JsonNode? value, out string? error)
    {
        value = null;
        error = null;

        // An empty input is an absent value; save-time validation decides whether the facet may be empty.
        if (string.IsNullOrEmpty(text) || (kind != AsterValueKind.String && string.IsNullOrWhiteSpace(text)))
            return true;

        switch (kind)
        {
            case AsterValueKind.Integer:
                if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    || long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out integer))
                    return Ok(JsonValue.Create(integer), out value);
                error = "Enter a whole number.";
                return false;

            case AsterValueKind.Number:
                // The invariant attempt allows no group separator, so a decimal comma ("1,5" in de-DE) falls through to the
                // user's culture instead of being read as "15".
                if (decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || decimal.TryParse(text.Trim(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.CurrentCulture, out number))
                    return Ok(JsonValue.Create(number), out value);
                error = "Enter a number.";
                return false;

            case AsterValueKind.Boolean:
                if (bool.TryParse(text.Trim(), out var boolean))
                    return Ok(JsonValue.Create(boolean), out value);
                error = "Enter true or false.";
                return false;

            case AsterValueKind.DateTime:
                if (TryParseInstant(text, out var instant))
                {
                    // A date hint stores the date at midnight UTC (docs/ui-hints.md).
                    if (AsterUiHints.Normalize(uiHint) == AsterUiHints.Date)
                        instant = instant.Date;
                    return Ok(JsonValue.Create(instant.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)), out value);
                }

                error = "Enter a date and time.";
                return false;

            case AsterValueKind.Guid:
                if (Guid.TryParse(text.Trim(), out var guid))
                    return Ok(JsonValue.Create(guid.ToString("D")), out value);
                error = "Enter a GUID, such as 3f2504e0-4f89-11d3-9a0c-0305e82c3301.";
                return false;

            case AsterValueKind.Reference:
                return Ok(JsonValue.Create(text.Trim()), out value);

            default:
                return Ok(JsonValue.Create(text), out value);
        }
    }

    /// <summary>
    /// Reads a date and time as UTC. Text without an offset (what the date-time picker writes, in the current
    /// culture) is taken as UTC, so a value round-trips through the picker unchanged.
    /// </summary>
    private static bool TryParseInstant(string? text, out DateTime instant)
    {
        const DateTimeStyles Styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        var trimmed = text?.Trim();

        // ISO text (payloads, and what ToEditorValue writes) is unambiguous. Anything else is the picker's text in the
        // user's culture, read before the invariant culture so en-GB "04/03/2026" stays 4 March.
        if (trimmed is not null && IsoDate().IsMatch(trimmed))
            return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, Styles, out instant);

        if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, Styles, out instant)
            || DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, Styles, out instant))
            return true;

        instant = default;
        return false;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDate();

    private static bool TryParseJson(string? text, out JsonNode? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        try
        {
            value = JsonNode.Parse(text);
            return true;
        }
        catch (JsonException exception)
        {
            error = $"Enter valid JSON: {exception.Message}";
            return false;
        }
    }

    private static bool Ok(JsonNode? converted, out JsonNode? value)
    {
        value = converted;
        return true;
    }
}
