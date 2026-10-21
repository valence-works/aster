using System.Text.Json.Nodes;

namespace Aster.Elsa.Studio.Forms;

/// <summary>
/// The values of one resource being edited: one JSON value per field of an <see cref="AsterFormModel"/>, plus the
/// input errors of editor values that don't fit their facet type. Builds the <c>aspects</c> object of
/// <c>POST /resources</c> and <c>PUT /resources/{id}</c>.
/// </summary>
internal sealed class AsterFormState
{
    private readonly Dictionary<string, JsonNode?> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode?> loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> storedKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> inputErrors = new(StringComparer.Ordinal);
    private readonly JsonObject original;

    private AsterFormState(AsterFormModel model, JsonObject original)
    {
        Model = model;
        this.original = original;
    }

    /// <summary>Gets the form the values belong to.</summary>
    public AsterFormModel Model { get; }

    /// <summary>Gets the input errors by field key: editor values that couldn't be converted to the facet type.</summary>
    public IReadOnlyDictionary<string, string> InputErrors => inputErrors;

    /// <summary>
    /// Creates the state of a new resource (<paramref name="aspects"/> <see langword="null"/>: each facet's
    /// <c>default</c>) or of an existing resource version.
    /// </summary>
    public static AsterFormState Create(AsterFormModel model, JsonObject? aspects = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var state = new AsterFormState(model, aspects?.DeepClone().AsObject() ?? []);
        foreach (var field in model.Fields)
        {
            var aspect = aspects?[field.AspectKey] as JsonObject;
            var key = StoredKey(aspect, field.FacetId, model.FindField(field.AspectKey, ToCamelCase(field.FacetId)) is not null);
            state.storedKeys[field.Key] = key;
            state.loaded[field.Key] = aspect?[key]?.DeepClone();
            state.values[field.Key] = aspects is null ? field.DefaultValue?.DeepClone() : aspect?[key]?.DeepClone();
        }

        return state;
    }

    /// <summary>Gets a field's current payload value.</summary>
    public JsonNode? GetValue(AsterFormField field) => values.GetValueOrDefault(field.Key);

    /// <summary>Gets the value to hand to a field's editor.</summary>
    public object? GetEditorValue(AsterFormField field, AsterFieldEditor editor) =>
        AsterFieldValues.ToEditorValue(field, editor, GetValue(field));

    /// <summary>
    /// Applies what a field's editor wrote. Returns <see langword="false"/> and records an input error when the
    /// value doesn't fit the facet type; the previous value is kept until the input is fixed.
    /// </summary>
    public bool SetEditorValue(AsterFormField field, AsterFieldEditor editor, object? input)
    {
        if (!AsterFieldValues.TryFromEditorValue(field, editor, input, out var value, out var error))
        {
            inputErrors[field.Key] = error!;
            return false;
        }

        inputErrors.Remove(field.Key);
        values[field.Key] = value;
        return true;
    }

    /// <summary>
    /// Builds the <c>aspects</c> object to save: every aspect with a field that differs from the loaded version (for a
    /// new resource, every aspect with a value), with every field (empty fields as <see langword="null"/>). Untouched
    /// aspects are left out, so an update keeps them as they are and the server doesn't validate them again, and a
    /// create doesn't turn an absent aspect into one of nulls. Facets the loaded version holds but the form doesn't
    /// know are kept, because an update replaces each aspect it names whole.
    /// </summary>
    public JsonObject BuildAspects()
    {
        var aspects = new JsonObject();
        foreach (var section in Model.Sections)
        {
            if (section.Fields.All(field => JsonNode.DeepEquals(values.GetValueOrDefault(field.Key), loaded.GetValueOrDefault(field.Key))))
                continue;

            var payload = original[section.AspectKey] is JsonObject existing ? existing.DeepClone().AsObject() : [];
            // Each value goes back under the key it was read from. Any other key the aspect holds is kept, as other
            // unknown facets are: the form never writes a second spelling, so there is nothing of its own to clean up.
            foreach (var field in section.Fields)
                payload[storedKeys[field.Key]] = values.GetValueOrDefault(field.Key)?.DeepClone();

            aspects[section.AspectKey] = payload;
        }

        return aspects;
    }

    // The server reads a facet from its ID or, when the payload has no such key, from the camel-case form (how typed
    // aspects are stored: Title as title). The form reads and writes the same key, except when the camel-case form is
    // itself a facet of the aspect: that key is the other facet's, so each keeps its own.
    private static string StoredKey(JsonObject? aspect, string facetId, bool camelIsAFacet)
    {
        var camel = ToCamelCase(facetId);
        return aspect is not null && !camelIsAFacet && !aspect.ContainsKey(facetId) && aspect.ContainsKey(camel) ? camel : facetId;
    }

    // As the server's AspectFacetValueResolver: the first character lower-cased, the rest unchanged.
    private static string ToCamelCase(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
