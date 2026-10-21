namespace Aster.Core.Models.Definitions;

/// <summary>
/// The standard <see cref="FacetMetadata.UiHint"/> vocabulary (#107). Hints stay opaque to Aster, which never
/// validates or interprets them; these names exist so that aspects render the same way in every host UI.
/// Hosts should fall back to an editor chosen from the facet type for hints they don't recognize. See
/// <c>docs/ui-hints.md</c> for the expected editors and the facet types each hint applies to.
/// </summary>
public static class UiHints
{
    /// <summary>A single-line text input. Applies to <c>string</c>.</summary>
    public const string Text = "text";

    /// <summary>A plain multi-line text area. Applies to <c>string</c>.</summary>
    public const string Multiline = "multiline";

    /// <summary>A Markdown editor; the value is Markdown source. Applies to <c>string</c>.</summary>
    public const string Markdown = "markdown";

    /// <summary>A rich-text (WYSIWYG) editor; the value is HTML. Applies to <c>string</c>.</summary>
    public const string Html = "html";

    /// <summary>A URL slug input that offers to derive the value from a title. Applies to <c>string</c>.</summary>
    public const string Slug = "slug";

    /// <summary>An email address input. Applies to <c>string</c>.</summary>
    public const string Email = "email";

    /// <summary>A URL input. Applies to <c>string</c>.</summary>
    public const string Url = "url";

    /// <summary>A numeric input honoring <c>Minimum</c> and <c>Maximum</c>. Applies to <c>int</c> and <c>decimal</c>.</summary>
    public const string Number = "number";

    /// <summary>A dropdown over <c>AllowedValues</c>; a multi-select for arrays. Applies to scalars and their arrays.</summary>
    public const string Select = "select";

    /// <summary>A radio group over <c>AllowedValues</c>. Applies to <c>string</c>, <c>int</c> and <c>decimal</c>.</summary>
    public const string Radio = "radio";

    /// <summary>
    /// A checkbox for <c>bool</c>, or a checkbox list over <c>AllowedValues</c> for arrays such as <c>string[]</c>.
    /// </summary>
    public const string Checkbox = "checkbox";

    /// <summary>An on/off switch. Applies to <c>bool</c>.</summary>
    public const string Switch = "switch";

    /// <summary>A date picker; the value is a date at midnight UTC. Applies to <c>datetime</c>.</summary>
    public const string Date = "date";

    /// <summary>A date and time picker. Applies to <c>datetime</c>.</summary>
    public const string DateTime = "datetime";

    /// <summary>A free-entry tag input. Applies to <c>string[]</c>.</summary>
    public const string Tags = "tags";

    /// <summary>
    /// A picker that searches resources of the facet's <c>TargetDefinitionIds</c>. Applies to <c>reference</c> and
    /// <c>reference[]</c>.
    /// </summary>
    public const string ReferencePicker = "reference-picker";

    /// <summary>A JSON editor. Applies to <c>object</c>, and to <c>string</c> holding JSON text.</summary>
    public const string Json = "json";

    /// <summary>A code editor with monospace text. Applies to <c>string</c>.</summary>
    public const string Code = "code";

    /// <summary>
    /// A masked input. Applies to <c>string</c>. Presentation only: Aster stores and returns the value as is, so
    /// don't use it for credentials that need protection at rest.
    /// </summary>
    public const string Secret = "secret";

    /// <summary>Every standard hint, in the order listed in <c>docs/ui-hints.md</c>.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Text, Multiline, Markdown, Html, Slug, Email, Url, Number, Select, Radio, Checkbox, Switch,
        Date, DateTime, Tags, ReferencePicker, Json, Code, Secret,
    ];
}
