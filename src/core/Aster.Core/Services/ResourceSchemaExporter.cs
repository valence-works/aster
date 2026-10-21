using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IResourceSchemaExporter"/>. Maps facet types and metadata onto JSON Schema 2020-12 keywords
/// so that every value <see cref="FacetValueValidator"/> accepts also validates against the schema.
/// </summary>
internal sealed class ResourceSchemaExporter : IResourceSchemaExporter
{
    /// <summary>The JSON Schema dialect of exported documents.</summary>
    public const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    /// <inheritdoc />
    public JsonObject Export(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var aspects = new JsonObject();
        foreach (var (key, aspect) in definition.AspectDefinitions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            // Save-time validation skips an absent or null aspect payload, so the aspect may be null.
            aspects[key] = AspectSchema(aspect, nullable: true);
        }

        return Detach(new JsonObject
        {
            ["$schema"] = SchemaDialect,
            ["title"] = definition.DefinitionId,
            ["type"] = "object",
            ["properties"] = aspects,
            ["x-aster-definition"] = new JsonObject
            {
                ["definitionId"] = definition.DefinitionId,
                ["version"] = definition.Version,
            },
        });
    }

    /// <inheritdoc />
    public JsonObject ExportAspect(AspectDefinition aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);

        var schema = new JsonObject { ["$schema"] = SchemaDialect, ["title"] = aspect.AspectDefinitionId };
        foreach (var (name, value) in AspectSchema(aspect, nullable: false).ToList())
            schema[name] = value?.DeepClone();

        return Detach(schema);
    }

    // Values created from CLR objects need a type-info resolver to serialize; on .NET 8 that makes
    // ToJsonString(options) throw for options without one. Reparsing yields JSON-backed values that write with
    // any options.
    private static JsonObject Detach(JsonObject schema) => JsonNode.Parse(schema.ToJsonString())!.AsObject();

    private static JsonObject AspectSchema(AspectDefinition aspect, bool nullable)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var ordered = aspect.FacetDefinitions
            .Select(static (facet, index) => (facet, index))
            .OrderBy(static item => item.facet.Metadata?.DisplayOrder ?? int.MaxValue)
            .ThenBy(static item => item.index)
            .Select(static item => item.facet);

        foreach (var facet in ordered)
        {
            properties[facet.FacetDefinitionId] = FacetSchema(facet);
            if (facet.IsRequired)
                required.Add(facet.FacetDefinitionId);
        }

        var schema = new JsonObject
        {
            ["type"] = Type("object", nullable),
            ["properties"] = properties,
        };

        if (required.Count > 0)
            schema["required"] = required;

        schema["x-aster-aspect"] = new JsonObject
        {
            ["aspectDefinitionId"] = aspect.AspectDefinitionId,
            ["version"] = aspect.Version,
            ["shared"] = aspect.IsShared,
            ["requiresName"] = aspect.RequiresName,
        };

        return schema;
    }

    private static JsonObject FacetSchema(FacetDefinition facet)
    {
        var descriptor = ParseFacetType(facet.Type);
        var metadata = facet.Metadata;
        var schema = new JsonObject();

        if (metadata?.Label is { } label)
            schema["title"] = label;
        if (metadata?.Description is { } description)
            schema["description"] = description;

        // Unknown (legacy) types are not validated on save, so the schema accepts any value.
        if (descriptor.IsKnown)
        {
            // Absent and null are the same to save-time validation: an optional facet may be null; a required one
            // must be present and non-null. Array elements are never null.
            var nullable = !facet.IsRequired;
            if (descriptor.IsArray)
            {
                schema["type"] = Type("array", nullable);
                var items = new JsonObject();
                AddScalar(items, descriptor.Element, metadata, nullable: false);
                schema["items"] = items;

                // Required means at least one element.
                var minItems = Math.Max(metadata?.MinItems ?? 0, facet.IsRequired ? 1 : 0);
                if (minItems > 0)
                    schema["minItems"] = minItems;
                if (metadata?.MaxItems is { } maxItems)
                    schema["maxItems"] = maxItems;
                if (metadata?.UniqueItems is true)
                    schema["uniqueItems"] = true;
            }
            else
            {
                AddScalar(schema, descriptor.Element, metadata, nullable);
            }
        }

        if (metadata?.DefaultValue is { } defaultValue)
            schema["default"] = ToNode(defaultValue);

        schema["x-aster-type"] = facet.Type;
        if (metadata?.UiHint is { } uiHint)
            schema["x-aster-uiHint"] = uiHint;
        if (metadata?.DisplayOrder is { } displayOrder)
            schema["x-aster-displayOrder"] = displayOrder;

        if (descriptor.Element == FacetValueType.Reference
            && (metadata?.TargetDefinitionIds is not null || metadata?.ReferenceResolution is not null))
        {
            var reference = new JsonObject();
            if (metadata.TargetDefinitionIds is { } targets)
                reference["targetDefinitionIds"] = new JsonArray(targets.Select(static target => (JsonNode?)target).ToArray());
            if (metadata.ReferenceResolution is { } resolution)
                reference["resolution"] = resolution;
            schema["x-aster-reference"] = reference;
        }

        return schema;
    }

    /// <summary>Adds the type keywords and value constraints of one scalar value (a facet or an array element).</summary>
    private static void AddScalar(JsonObject schema, FacetValueType type, FacetMetadata? metadata, bool nullable)
    {
        switch (type)
        {
            case FacetValueType.String:
                schema["type"] = Type("string", nullable);
                AddEnum(schema, metadata, nullable);
                if (metadata?.MinLength is { } minLength)
                    schema["minLength"] = minLength;
                if (metadata?.MaxLength is { } maxLength)
                    schema["maxLength"] = maxLength;
                if (metadata?.Pattern is { } pattern)
                    schema["pattern"] = pattern;
                break;

            case FacetValueType.Int:
            case FacetValueType.Decimal:
                schema["type"] = Type(type == FacetValueType.Int ? "integer" : "number", nullable);
                AddEnum(schema, metadata, nullable);
                if (metadata?.Minimum is { } minimum)
                    schema["minimum"] = minimum;
                if (metadata?.Maximum is { } maximum)
                    schema["maximum"] = maximum;
                break;

            case FacetValueType.Bool:
                schema["type"] = Type("boolean", nullable);
                break;

            case FacetValueType.DateTime:
                schema["type"] = Type("string", nullable);
                schema["format"] = "date-time";
                AddAllowedValuesAnnotation(schema, metadata);
                break;

            case FacetValueType.Guid:
                schema["type"] = Type("string", nullable);
                schema["format"] = "uuid";
                AddAllowedValuesAnnotation(schema, metadata);
                break;

            case FacetValueType.Reference:
                // A reference is a non-blank resource ID.
                schema["type"] = Type("string", nullable);
                AddEnum(schema, metadata, nullable);
                schema["pattern"] = "\\S";
                break;

            case FacetValueType.Object:
                schema["type"] = Type("object", nullable);
                break;
        }
    }

    // Allowed values compare exactly in JSON Schema, which matches Aster for strings, numbers and references.
    private static void AddEnum(JsonObject schema, FacetMetadata? metadata, bool nullable)
    {
        if (metadata?.AllowedValues is not { Count: > 0 } allowed)
            return;

        var values = new JsonArray(allowed.Select(ToNode).ToArray());
        if (nullable)
            values.Add(null);
        schema["enum"] = values;
    }

    // Aster compares guids case-insensitively and datetimes as instants, so an enum would reject values it accepts.
    private static void AddAllowedValuesAnnotation(JsonObject schema, FacetMetadata? metadata)
    {
        if (metadata?.AllowedValues is { Count: > 0 } allowed)
            schema["x-aster-allowedValues"] = new JsonArray(allowed.Select(ToNode).ToArray());
    }

    private static JsonNode Type(string type, bool nullable) =>
        nullable ? new JsonArray(type, "null") : JsonValue.Create(type)!;

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        _ => JsonSerializer.SerializeToNode(value, value.GetType()),
    };
}
