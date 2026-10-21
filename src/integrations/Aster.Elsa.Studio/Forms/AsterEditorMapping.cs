using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Studio.UIHints;

namespace Aster.Elsa.Studio.Forms;

/// <summary>
/// The standard Aster UI hint names (#107), mirrored from <c>Aster.Core.Models.Definitions.UiHints</c> so the module
/// needs no reference to Aster.Core. <c>docs/ui-hints.md</c> describes each one.
/// </summary>
internal static class AsterUiHints
{
    public const string Text = "text";
    public const string Multiline = "multiline";
    public const string Markdown = "markdown";
    public const string Html = "html";
    public const string Slug = "slug";
    public const string Email = "email";
    public const string Url = "url";
    public const string Number = "number";
    public const string Select = "select";
    public const string Radio = "radio";
    public const string Checkbox = "checkbox";
    public const string Switch = "switch";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Tags = "tags";
    public const string ReferencePicker = "reference-picker";
    public const string Json = "json";
    public const string Code = "code";
    public const string Secret = "secret";

    // Names Aster Studio used before the standard vocabulary; aspects seeded earlier still carry them.
    private static readonly Dictionary<string, string> Legacy = new(StringComparer.Ordinal) { ["toggle"] = Switch, ["richtext"] = Markdown };

    /// <summary>Returns the standard name for a legacy hint, or the hint itself.</summary>
    public static string? Normalize(string? hint) => hint is not null && Legacy.TryGetValue(hint, out var standard) ? standard : hint;
}

/// <summary>How a field's value travels between the form state and an Elsa Studio input editor.</summary>
internal enum AsterEditorValueShape
{
    /// <summary>A string (single line, multi-line, dropdown, radio list, code, and JSON text in a string facet).</summary>
    Text,

    /// <summary>A <see cref="bool"/> (checkbox).</summary>
    Boolean,

    /// <summary>A date and time string the date-time picker parses and formats in the current culture.</summary>
    DateTime,

    /// <summary>A list of strings, one per array element (multi-text, check list).</summary>
    List,

    /// <summary>JSON text for any JSON value (JSON editor on an object, a legacy facet or an object array).</summary>
    Json,

    /// <summary>A resource ID, or a list of them for <c>reference[]</c> (the Aster reference picker).</summary>
    Reference,
}

/// <summary>The Elsa Studio input editor a field renders with.</summary>
/// <param name="UiHint">The Elsa Studio UI hint resolved through <c>IUIHintService</c>, such as <c>singleline</c>.</param>
/// <param name="Shape">How the value travels to and from the editor.</param>
/// <param name="UISpecifications">The editor's <c>InputDescriptor.UISpecifications</c>, such as dropdown items.</param>
/// <param name="IsFallback">Whether the editor was chosen from the facet type rather than from its UI hint.</param>
internal sealed record AsterFieldEditor(
    string UiHint,
    AsterEditorValueShape Shape,
    IDictionary<string, object>? UISpecifications,
    bool IsFallback);

/// <summary>
/// Maps an Aster facet's <c>x-aster-uiHint</c> onto one of Elsa Studio's existing UI-hint input editors
/// (<see cref="InputUIHints"/>), with a fallback per facet type when the facet has no hint, an unknown one, or one
/// that doesn't apply to its type (#107, <c>docs/ui-hints.md</c>). Reference facets use the module's own
/// <see cref="ReferencePickerHint"/> editor, since Elsa Studio has none.
/// </summary>
internal static class AsterEditorMapping
{
    /// <summary>The Elsa Studio UI hint of the module's reference picker (<c>AsterReferencePickerHandler</c>).</summary>
    public const string ReferencePickerHint = "aster-reference-picker";

    /// <summary>The <c>UISpecifications</c> key carrying the reference picker's options.</summary>
    public const string ReferenceSpecificationKey = "aster-reference";

    /// <summary>Chooses the editor for <paramref name="field"/>.</summary>
    public static AsterFieldEditor Resolve(AsterFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return FromHint(field, AsterUiHints.Normalize(field.UiHint)) ?? Fallback(field);
    }

    private static AsterFieldEditor? FromHint(AsterFormField field, string? hint)
    {
        var scalar = !field.IsArray;
        var hasOptions = field.AllowedValues.Count > 0;
        var kind = field.Kind;

        return hint switch
        {
            AsterUiHints.Text or AsterUiHints.Slug or AsterUiHints.Email or AsterUiHints.Url or AsterUiHints.Secret
                when scalar && kind == AsterValueKind.String => Editor(InputUIHints.SingleLine, AsterEditorValueShape.Text),
            AsterUiHints.Multiline or AsterUiHints.Markdown or AsterUiHints.Html
                when scalar && kind == AsterValueKind.String => Editor(InputUIHints.MultiLine, AsterEditorValueShape.Text),
            AsterUiHints.Code when scalar && kind == AsterValueKind.String => Editor(InputUIHints.CodeEditor, AsterEditorValueShape.Text),
            AsterUiHints.Number when scalar && kind is AsterValueKind.Integer or AsterValueKind.Number => Editor(InputUIHints.SingleLine, AsterEditorValueShape.Text),
            AsterUiHints.Select when hasOptions => scalar ? DropDown(field) : CheckList(field),
            AsterUiHints.Radio when hasOptions && scalar && kind is AsterValueKind.String or AsterValueKind.Integer or AsterValueKind.Number => RadioList(field),
            AsterUiHints.Checkbox when scalar && kind == AsterValueKind.Boolean => Editor(InputUIHints.Checkbox, AsterEditorValueShape.Boolean),
            AsterUiHints.Checkbox when !scalar && hasOptions => CheckList(field),

            // Elsa's "switch-editor" is the Switch activity's case editor, not an on/off toggle, so a switch renders as a checkbox.
            AsterUiHints.Switch when scalar && kind == AsterValueKind.Boolean => Editor(InputUIHints.Checkbox, AsterEditorValueShape.Boolean),
            AsterUiHints.Date or AsterUiHints.DateTime when scalar && kind == AsterValueKind.DateTime => Editor(InputUIHints.DateTimePicker, AsterEditorValueShape.DateTime),
            AsterUiHints.Tags when !scalar && kind == AsterValueKind.String => Editor(InputUIHints.MultiText, AsterEditorValueShape.List),
            AsterUiHints.ReferencePicker when kind == AsterValueKind.Reference => ReferencePicker(field),
            AsterUiHints.Json when kind is AsterValueKind.Object or AsterValueKind.Any => JsonEditor(),
            AsterUiHints.Json when scalar && kind == AsterValueKind.String => Editor(InputUIHints.JsonEditor, AsterEditorValueShape.Text, CodeLanguage("json")),
            _ => null,
        };
    }

    /// <summary>The fallback per facet type, as <c>docs/ui-hints.md</c> asks of every host.</summary>
    private static AsterFieldEditor Fallback(AsterFormField field)
    {
        // AllowedValues win over free entry.
        if (field.AllowedValues.Count > 0 && field.Kind != AsterValueKind.Boolean)
            return (field.IsArray ? CheckList(field) : DropDown(field)) with { IsFallback = true };

        AsterFieldEditor editor = (field.Kind, field.IsArray) switch
        {
            (AsterValueKind.Reference, _) => ReferencePicker(field),
            (AsterValueKind.Object or AsterValueKind.Any, _) => JsonEditor(),
            (_, true) => Editor(InputUIHints.MultiText, AsterEditorValueShape.List),
            (AsterValueKind.Boolean, false) => Editor(InputUIHints.Checkbox, AsterEditorValueShape.Boolean),
            (AsterValueKind.DateTime, false) => Editor(InputUIHints.DateTimePicker, AsterEditorValueShape.DateTime),
            _ => Editor(InputUIHints.SingleLine, AsterEditorValueShape.Text),
        };

        return editor with { IsFallback = true };
    }

    private static AsterFieldEditor Editor(string hint, AsterEditorValueShape shape, IDictionary<string, object>? specifications = null) =>
        new(hint, shape, specifications, IsFallback: false);

    private static AsterFieldEditor JsonEditor() => Editor(InputUIHints.JsonEditor, AsterEditorValueShape.Json, CodeLanguage("json"));

    private static Dictionary<string, object> CodeLanguage(string language) =>
        Specification("code-editor", new JsonObject { ["language"] = language });

    private static AsterFieldEditor DropDown(AsterFormField field)
    {
        var items = Options(field).Select(static option => new JsonObject { ["text"] = option, ["value"] = option });
        var selectList = new JsonObject { ["items"] = new JsonArray(items.ToArray<JsonNode?>()), ["isFlagsEnum"] = false, ["sortItems"] = false };
        return Editor(InputUIHints.DropDown, AsterEditorValueShape.Text, Specification("dropdown", new JsonObject { ["selectList"] = selectList }));
    }

    private static AsterFieldEditor RadioList(AsterFormField field)
    {
        var items = Options(field).Select(static option => new JsonObject { ["text"] = option, ["value"] = option });
        var radioList = new JsonObject { ["items"] = new JsonArray(items.ToArray<JsonNode?>()), ["isFlagsEnum"] = false };
        return Editor(InputUIHints.RadioList, AsterEditorValueShape.Text, Specification("radiolist", new JsonObject { ["radioList"] = radioList }));
    }

    private static AsterFieldEditor CheckList(AsterFormField field)
    {
        var items = Options(field).Select(static option => new JsonObject { ["text"] = option, ["value"] = option, ["isChecked"] = false });
        var checkList = new JsonObject { ["items"] = new JsonArray(items.ToArray<JsonNode?>()), ["isFlagsEnum"] = false };
        return Editor(InputUIHints.CheckList, AsterEditorValueShape.List, Specification("checklist", new JsonObject { ["checkList"] = checkList }));
    }

    private static AsterFieldEditor ReferencePicker(AsterFormField field)
    {
        var options = new JsonObject
        {
            ["targetDefinitionIds"] = new JsonArray(field.ReferenceTargets.Select(static target => (JsonNode?)target).ToArray()),
            ["multiple"] = field.IsArray,
        };
        return Editor(ReferencePickerHint, AsterEditorValueShape.Reference, Specification(ReferenceSpecificationKey, options));
    }

    private static IEnumerable<string> Options(AsterFormField field) =>
        field.AllowedValues.Select(AsterFieldValues.ToText);

    // Elsa Studio's editors read UISpecifications entries as JsonElement (camelCase), as its server sends them.
    private static Dictionary<string, object> Specification(string key, JsonObject value) =>
        new(StringComparer.Ordinal) { [key] = JsonSerializer.SerializeToElement(value) };
}
