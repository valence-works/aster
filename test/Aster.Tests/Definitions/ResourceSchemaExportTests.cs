using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Services;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #106: resource definitions export as JSON Schema 2020-12 documents that validate against the meta-schema,
/// are deterministic, and never reject a resource that save-time validation accepts.
/// </summary>
public sealed class ResourceSchemaExportTests
{
    private static readonly ResourceSchemaExporter Exporter = new();

    private static FacetDefinition Facet(string id, string type, bool required = false, FacetMetadata? metadata = null) => new()
    {
        FacetDefinitionId = id,
        Id = $"facet-{id}",
        Version = 1,
        Type = type,
        IsRequired = required,
        Metadata = metadata,
    };

    private static AspectDefinition Aspect(string id, params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = id,
        Id = $"aspect-{id}",
        Version = 1,
        FacetDefinitions = facets,
    };

    private static ResourceDefinition Definition(params AspectDefinition[] aspects) => new()
    {
        DefinitionId = "Product",
        Id = "definition-product",
        Version = 3,
        AspectDefinitions = aspects.ToDictionary(static aspect => aspect.AspectDefinitionId),
    };

    /// <summary>Every facet type, with every constraint the exporter maps.</summary>
    private static readonly ResourceDefinition Catalog = Definition(
        Aspect("Product",
            Facet("Name", "string", required: true, new FacetMetadata
            {
                Label = "Name", Description = "Lower-case name", DisplayOrder = 1, UiHint = "text",
                MinLength = 2, MaxLength = 10, Pattern = "^[a-z]+$",
            }),
            Facet("Color", "string", metadata: new FacetMetadata { AllowedValues = ["red", "green"], DefaultValue = "red" }),
            Facet("Quantity", "int", metadata: new FacetMetadata { Minimum = 0, Maximum = 100 }),
            Facet("Price", "decimal", metadata: new FacetMetadata { Minimum = 0.5m, AllowedValues = [0.5m, 1.25m, 10m] }),
            Facet("Active", "bool"),
            Facet("ReleasedAt", "datetime"),
            Facet("Sku", "guid", metadata: new FacetMetadata { AllowedValues = ["0f8fad5b-d9cb-469f-a165-70867728950e"] }),
            Facet("Supplier", "reference", metadata: new FacetMetadata { TargetDefinitionIds = ["Supplier"], ReferenceResolution = "live" }),
            Facet("Tags", "string[]", required: true, new FacetMetadata { MaxItems = 3, UniqueItems = true, MaxLength = 5 }),
            Facet("Scores", "int[]", metadata: new FacetMetadata { Minimum = 1, Maximum = 5, MinItems = 2 }),
            Facet("Extra", "object")),
        Aspect("Seo", Facet("Slug", "string", required: true)));

    [Fact]
    public void Export_ProducesTheDocumentedShape()
    {
        var definition = Definition(
            Aspect("Seo",
                Facet("Slug", "string", required: true, new FacetMetadata { Pattern = "^[a-z-]+$", DisplayOrder = 2 }),
                Facet("Title", "string", metadata: new FacetMetadata { Label = "Title", MaxLength = 60, DisplayOrder = 1, UiHint = "text" })),
            Aspect("Links",
                Facet("Related", "reference[]", metadata: new FacetMetadata { TargetDefinitionIds = ["Article"], UniqueItems = true })) with
            {
                IsShared = true,
            });

        var actual = Exporter.Export(definition).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Equal(
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "Product",
              "type": "object",
              "properties": {
                "Links": {
                  "type": [
                    "object",
                    "null"
                  ],
                  "properties": {
                    "Related": {
                      "type": [
                        "array",
                        "null"
                      ],
                      "items": {
                        "type": "string",
                        "pattern": "\\S"
                      },
                      "uniqueItems": true,
                      "x-aster-type": "reference[]",
                      "x-aster-reference": {
                        "targetDefinitionIds": [
                          "Article"
                        ]
                      }
                    }
                  },
                  "x-aster-aspect": {
                    "aspectDefinitionId": "Links",
                    "version": 1,
                    "shared": true,
                    "requiresName": false
                  }
                },
                "Seo": {
                  "type": [
                    "object",
                    "null"
                  ],
                  "properties": {
                    "Title": {
                      "title": "Title",
                      "type": [
                        "string",
                        "null"
                      ],
                      "maxLength": 60,
                      "x-aster-type": "string",
                      "x-aster-uiHint": "text",
                      "x-aster-displayOrder": 1
                    },
                    "Slug": {
                      "type": "string",
                      "pattern": "^[a-z-]+$",
                      "x-aster-type": "string",
                      "x-aster-displayOrder": 2
                    }
                  },
                  "required": [
                    "Slug"
                  ],
                  "x-aster-aspect": {
                    "aspectDefinitionId": "Seo",
                    "version": 1,
                    "shared": false,
                    "requiresName": false
                  }
                }
              },
              "x-aster-definition": {
                "definitionId": "Product",
                "version": 3
              }
            }
            """.ReplaceLineEndings("\n"),
            actual.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Export_IsDeterministic()
    {
        var reordered = Catalog with
        {
            AspectDefinitions = Catalog.AspectDefinitions.Reverse().ToDictionary(static pair => pair.Key, static pair => pair.Value),
        };

        Assert.Equal(Exporter.Export(Catalog).ToJsonString(), Exporter.Export(reordered).ToJsonString());
        Assert.Equal(Exporter.Export(Catalog).ToJsonString(), Exporter.Export(Catalog).ToJsonString());
    }

    [Fact]
    public void ExportedSchemas_ValidateAgainstThe202012MetaSchema()
    {
        foreach (var schema in new[] { Exporter.Export(Catalog), Exporter.ExportAspect(Catalog.AspectDefinitions["Product"]) })
        {
            var result = MetaSchemas.Draft202012.Evaluate(schema, new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.True(result.IsValid, schema.ToJsonString());
        }
    }

    [Fact]
    public void ExportAspect_DescribesANonNullPayload()
    {
        var schema = Exporter.ExportAspect(Catalog.AspectDefinitions["Seo"]);

        Assert.Equal(ResourceSchemaExporter.SchemaDialect, (string?)schema["$schema"]);
        Assert.Equal("object", (string?)schema["type"]);
        Assert.Equal("Slug", (string?)schema["required"]![0]);
    }

    [Fact]
    public void Exporter_IsRegisteredByAddAsterCore()
    {
        using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();

        Assert.IsType<ResourceSchemaExporter>(provider.GetRequiredService<IResourceSchemaExporter>());
    }

    private const string ValidProduct = """{ "Name": "abc", "Tags": ["a"] }""";

    /// <summary>
    /// Resource payloads with the verdict of save-time validation and of the exported schema. They agree on
    /// every constraint the schema expresses; the schema is looser only where its keywords are annotations
    /// (<c>format</c>) or where Aster normalizes before comparing.
    /// </summary>
    public static TheoryData<string, string, bool, bool> Cases => new()
    {
        { "minimal", $$"""{ "Product": {{ValidProduct}} }""", true, true },
        { "absent aspects", "{}", true, true },
        { "null aspect", """{ "Product": null }""", true, true },
        {
            "every facet set",
            """
            { "Product": { "Name": "abcdef", "Color": "green", "Quantity": 100, "Price": 1.25, "Active": false,
              "ReleasedAt": "2026-07-01T10:00:00Z", "Sku": "0f8fad5b-d9cb-469f-a165-70867728950e", "Supplier": "acme",
              "Tags": ["a", "b", "c"], "Scores": [1, 5], "Extra": { "any": [1, "x"] } },
              "Seo": { "Slug": "s" } }
            """,
            true, true
        },
        { "optional facets null", """{ "Product": { "Name": "abc", "Tags": ["a"], "Color": null, "Quantity": null, "Scores": null, "Extra": null } }""", true, true },
        { "integral decimal for int", """{ "Product": { "Name": "abc", "Tags": ["a"], "Quantity": 7.0 } }""", true, true },
        { "upper-case guid", """{ "Product": { "Name": "abc", "Tags": ["a"], "Sku": "0F8FAD5B-D9CB-469F-A165-70867728950E" } }""", true, true },
        { "price equal to an allowed value", """{ "Product": { "Name": "abc", "Tags": ["a"], "Price": 10.0 } }""", true, true },

        { "required missing", """{ "Product": { "Tags": ["a"] } }""", false, false },
        { "required null", """{ "Product": { "Name": null, "Tags": ["a"] } }""", false, false },
        { "required array empty", """{ "Product": { "Name": "abc", "Tags": [] } }""", false, false },
        { "too short", """{ "Product": { "Name": "a", "Tags": ["a"] } }""", false, false },
        { "too long", """{ "Product": { "Name": "abcdefghijk", "Tags": ["a"] } }""", false, false },
        { "pattern", """{ "Product": { "Name": "ABC", "Tags": ["a"] } }""", false, false },
        { "string type", """{ "Product": { "Name": 5, "Tags": ["a"] } }""", false, false },
        { "not allowed", """{ "Product": { "Name": "abc", "Tags": ["a"], "Color": "blue" } }""", false, false },
        { "below minimum", """{ "Product": { "Name": "abc", "Tags": ["a"], "Quantity": -1 } }""", false, false },
        { "above maximum", """{ "Product": { "Name": "abc", "Tags": ["a"], "Quantity": 101 } }""", false, false },
        { "fractional int", """{ "Product": { "Name": "abc", "Tags": ["a"], "Quantity": 1.5 } }""", false, false },
        { "numeric string for int", """{ "Product": { "Name": "abc", "Tags": ["a"], "Quantity": "1" } }""", false, false },
        { "decimal below minimum", """{ "Product": { "Name": "abc", "Tags": ["a"], "Price": 0.25 } }""", false, false },
        { "decimal not allowed", """{ "Product": { "Name": "abc", "Tags": ["a"], "Price": 2 } }""", false, false },
        { "bool type", """{ "Product": { "Name": "abc", "Tags": ["a"], "Active": "yes" } }""", false, false },
        { "guid type", """{ "Product": { "Name": "abc", "Tags": ["a"], "Sku": 42 } }""", false, false },
        { "blank reference", """{ "Product": { "Name": "abc", "Tags": ["a"], "Supplier": "   " } }""", false, false },
        { "reference type", """{ "Product": { "Name": "abc", "Tags": ["a"], "Supplier": 5 } }""", false, false },
        { "too many items", """{ "Product": { "Name": "abc", "Tags": ["a", "b", "c", "d"] } }""", false, false },
        { "duplicate items", """{ "Product": { "Name": "abc", "Tags": ["a", "a"] } }""", false, false },
        { "element too long", """{ "Product": { "Name": "abc", "Tags": ["abcdef"] } }""", false, false },
        { "null element", """{ "Product": { "Name": "abc", "Tags": ["a", null] } }""", false, false },
        { "array type", """{ "Product": { "Name": "abc", "Tags": "a" } }""", false, false },
        { "too few items", """{ "Product": { "Name": "abc", "Tags": ["a"], "Scores": [3] } }""", false, false },
        { "element out of range", """{ "Product": { "Name": "abc", "Tags": ["a"], "Scores": [1, 6] } }""", false, false },
        { "object type", """{ "Product": { "Name": "abc", "Tags": ["a"], "Extra": [1] } }""", false, false },
        { "second aspect required", """{ "Seo": {} }""", false, false },

        // format is an annotation in 2020-12, and Aster's datetime parsing accepts more than RFC 3339.
        { "unparseable datetime", """{ "Product": { "Name": "abc", "Tags": ["a"], "ReleasedAt": "not a date" } }""", false, true },
        { "malformed guid", """{ "Product": { "Name": "abc", "Tags": ["a"], "Sku": "not-a-guid" } }""", false, true },
        { "guid outside allowed values", """{ "Product": { "Name": "abc", "Tags": ["a"], "Sku": "6f9619ff-8b86-d011-b42d-00cf4fc964ff" } }""", false, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SchemaVerdict_MatchesSaveTimeValidation(string name, string resourceJson, bool asterValid, bool schemaValid)
    {
        using var document = JsonDocument.Parse(resourceJson);
        var aspects = document.RootElement.EnumerateObject()
            .Where(static property => property.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(static property => property.Name, static property => (object)property.Value.Clone());

        var diagnostics = FacetValueValidator.Validate(Catalog, aspects);
        var schemaResult = JsonSchema.FromText(Exporter.Export(Catalog).ToJsonString())
            .Evaluate(JsonNode.Parse(resourceJson), new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(asterValid == (diagnostics.Count == 0), $"{name}: Aster {string.Join(", ", diagnostics.Select(static d => d.Code))}");
        Assert.True(schemaValid == schemaResult.IsValid, $"{name}: schema valid = {schemaResult.IsValid}");

        // The guarantee hosts rely on: the schema never rejects what Aster accepts.
        Assert.True(!asterValid || schemaValid, name);
    }
}
