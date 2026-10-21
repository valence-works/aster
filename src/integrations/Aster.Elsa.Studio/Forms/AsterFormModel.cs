using System.Text.Json.Nodes;

namespace Aster.Elsa.Studio.Forms;

/// <summary>The value type of a facet, from <c>x-aster-type</c> or, without it, the JSON Schema <c>type</c> and <c>format</c>.</summary>
internal enum AsterValueKind
{
    /// <summary>A legacy facet type the schema doesn't constrain: any JSON value.</summary>
    Any,

    /// <summary><c>string</c>.</summary>
    String,

    /// <summary><c>int</c>: JSON <c>integer</c>.</summary>
    Integer,

    /// <summary><c>decimal</c>: JSON <c>number</c>.</summary>
    Number,

    /// <summary><c>bool</c>.</summary>
    Boolean,

    /// <summary><c>datetime</c>: a <c>date-time</c> string.</summary>
    DateTime,

    /// <summary><c>guid</c>: a <c>uuid</c> string.</summary>
    Guid,

    /// <summary><c>reference</c>: the <c>ResourceId</c> of another resource.</summary>
    Reference,

    /// <summary><c>object</c>.</summary>
    Object,
}

/// <summary>
/// A form generated from a definition's JSON Schema export (#106): one section per aspect, one field per facet, in
/// the schema's order (aspects by key, facets by <c>x-aster-displayOrder</c>). Nothing in it is specific to a
/// definition, so a definition registered at runtime gets a form without code changes.
/// </summary>
/// <param name="DefinitionId">The definition ID, from <c>x-aster-definition</c> or the schema title.</param>
/// <param name="DefinitionVersion">The definition version, from <c>x-aster-definition</c>.</param>
/// <param name="Sections">The aspect sections.</param>
internal sealed record AsterFormModel(string DefinitionId, int? DefinitionVersion, IReadOnlyList<AsterFormSection> Sections)
{
    /// <summary>Gets every field of every section.</summary>
    public IEnumerable<AsterFormField> Fields => Sections.SelectMany(static section => section.Fields);

    /// <summary>Finds a field by aspect key and facet ID (both case-sensitive, as in Aster).</summary>
    public AsterFormField? FindField(string aspectKey, string facetId) =>
        Fields.FirstOrDefault(field => field.AspectKey == aspectKey && field.FacetId == facetId);

    /// <summary>Builds the form for a definition's exported JSON Schema.</summary>
    /// <param name="schema">The document <c>GET /definitions/{id}/schema</c> returned.</param>
    public static AsterFormModel FromSchema(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var definition = schema["x-aster-definition"] as JsonObject;
        var definitionId = definition?["definitionId"]?.ToString() ?? schema["title"]?.ToString() ?? "";
        var definitionVersion = AsterJson.ReadInt(definition?["version"]);

        var sections = new List<AsterFormSection>();
        foreach (var (aspectKey, node) in schema["properties"] as JsonObject ?? [])
        {
            if (node is not JsonObject aspect)
                continue;

            var required = (aspect["required"] as JsonArray ?? [])
                .Select(static item => item?.ToString())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            var fields = new List<AsterFormField>();
            foreach (var (facetId, facetNode) in aspect["properties"] as JsonObject ?? [])
            {
                if (facetNode is JsonObject facet)
                    fields.Add(ReadField(aspectKey, facetId, facet, required.Contains(facetId)));
            }

            var info = aspect["x-aster-aspect"] as JsonObject;
            sections.Add(new AsterFormSection(
                aspectKey,
                info?["aspectDefinitionId"]?.ToString() ?? aspectKey,
                AsterJson.ReadBool(info?["shared"]),
                fields));
        }

        return new AsterFormModel(definitionId, definitionVersion, sections);
    }

    private static AsterFormField ReadField(string aspectKey, string facetId, JsonObject facet, bool isRequired)
    {
        var asterType = facet["x-aster-type"]?.ToString();
        var types = JsonTypes(facet["type"]);
        var isArray = asterType?.EndsWith("[]", StringComparison.Ordinal) ?? types.Contains("array");
        var element = isArray ? facet["items"] as JsonObject ?? [] : facet;
        var kind = KindOf(asterType is null ? null : asterType.EndsWith("[]", StringComparison.Ordinal) ? asterType[..^2] : asterType, element);

        // Enumerations: enum for strings, numbers and references; x-aster-allowedValues for guids and datetimes.
        var allowed = (element["enum"] as JsonArray ?? element["x-aster-allowedValues"] as JsonArray ?? facet["x-aster-allowedValues"] as JsonArray ?? [])
            .Where(static value => value is not null)
            .Select(static value => value!.DeepClone())
            .ToList();

        var reference = facet["x-aster-reference"] as JsonObject;
        return new AsterFormField(
            aspectKey,
            facetId,
            facet["title"]?.ToString() ?? facetId,
            facet["description"]?.ToString(),
            kind,
            isArray,
            isRequired,
            asterType,
            facet["x-aster-uiHint"]?.ToString(),
            allowed,
            (reference?["targetDefinitionIds"] as JsonArray ?? []).Select(static target => target?.ToString()).OfType<string>().ToList(),
            facet["default"]?.DeepClone());
    }

    private static AsterValueKind KindOf(string? asterType, JsonObject element) => asterType switch
    {
        "string" => AsterValueKind.String,
        "int" => AsterValueKind.Integer,
        "decimal" => AsterValueKind.Number,
        "bool" => AsterValueKind.Boolean,
        "datetime" => AsterValueKind.DateTime,
        "guid" => AsterValueKind.Guid,
        "reference" => AsterValueKind.Reference,
        "object" => AsterValueKind.Object,
        not null => AsterValueKind.Any,

        // A schema without x-aster-type (another exporter's, say): read the JSON type.
        null => JsonTypes(element["type"]).FirstOrDefault(static type => type != "null") switch
        {
            "string" => element["format"]?.ToString() switch
            {
                "date-time" => AsterValueKind.DateTime,
                "uuid" => AsterValueKind.Guid,
                _ => AsterValueKind.String,
            },
            "integer" => AsterValueKind.Integer,
            "number" => AsterValueKind.Number,
            "boolean" => AsterValueKind.Boolean,
            "object" => AsterValueKind.Object,
            _ => AsterValueKind.Any,
        },
    };

    private static List<string> JsonTypes(JsonNode? type) => type switch
    {
        JsonArray array => array.Select(static item => item?.ToString()).OfType<string>().ToList(),
        JsonValue value => [value.ToString()],
        _ => [],
    };
}

/// <summary>One aspect of the form.</summary>
/// <param name="AspectKey">The attachment key, which is the property name in the resource payload.</param>
/// <param name="AspectDefinitionId">The aspect definition ID.</param>
/// <param name="IsShared">Whether the aspect is a pinned shared aspect.</param>
/// <param name="Fields">The facet fields, in display order.</param>
internal sealed record AsterFormSection(string AspectKey, string AspectDefinitionId, bool IsShared, IReadOnlyList<AsterFormField> Fields)
{
    /// <summary>Gets the request-body path of the aspect payload, such as <c>aspects.Content</c>.</summary>
    public string RequestPath => $"aspects.{AspectKey}";
}

/// <summary>One facet of the form.</summary>
/// <param name="AspectKey">The aspect attachment key.</param>
/// <param name="FacetId">The facet ID.</param>
/// <param name="Label">The label: the facet's <c>title</c>, or its ID.</param>
/// <param name="Description">The facet's <c>description</c>.</param>
/// <param name="Kind">The value type, of the elements for an array facet.</param>
/// <param name="IsArray">Whether the facet holds an array.</param>
/// <param name="IsRequired">Whether the aspect lists the facet as required.</param>
/// <param name="AsterType">The declared facet type (<c>x-aster-type</c>), such as <c>reference[]</c>.</param>
/// <param name="UiHint">The facet's <c>x-aster-uiHint</c>, verbatim.</param>
/// <param name="AllowedValues">The allowed values, of the elements for an array facet; empty when unrestricted.</param>
/// <param name="ReferenceTargets">The definitions a reference facet may point to; empty when any.</param>
/// <param name="DefaultValue">The facet's <c>default</c>.</param>
internal sealed record AsterFormField(
    string AspectKey,
    string FacetId,
    string Label,
    string? Description,
    AsterValueKind Kind,
    bool IsArray,
    bool IsRequired,
    string? AsterType,
    string? UiHint,
    IReadOnlyList<JsonNode> AllowedValues,
    IReadOnlyList<string> ReferenceTargets,
    JsonNode? DefaultValue)
{
    /// <summary>Gets the field's key in the form, <c>aspectKey.facetId</c>.</summary>
    public string Key => $"{AspectKey}.{FacetId}";

    /// <summary>
    /// Gets the request-body path of the value in <c>POST /resources</c> and <c>PUT /resources/{id}</c>, such as
    /// <c>aspects.Content.Title</c>.
    /// </summary>
    public string RequestPath => $"aspects.{AspectKey}.{FacetId}";
}

/// <summary>Lenient readers for JSON values from the server.</summary>
internal static class AsterJson
{
    public static bool ReadBool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    public static int? ReadInt(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;
}
