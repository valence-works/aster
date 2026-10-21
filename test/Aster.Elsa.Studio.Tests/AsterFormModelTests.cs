using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Client;
using Aster.Elsa.Studio.Forms;

namespace Aster.Elsa.Studio.Tests;

/// <summary>
/// #117: a definition registered at runtime yields an editable form from its JSON Schema export (#106), and the form
/// saves through the resource endpoints, with no code for the definition anywhere in the module.
/// </summary>
public sealed class AsterFormModelTests
{
    [Fact]
    public async Task DefinitionRegisteredAtRuntime_YieldsFormFromItsSchema()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        Assert.Empty(await client.ListDefinitionsAsync());

        // An administrator defines a new resource type while the Studio is running.
        await server.RegisterAsync(AsterTestServer.Product());

        var definitions = await client.ListDefinitionsAsync();
        var product = Assert.Single(definitions);
        Assert.Equal("Product", product.DefinitionId);
        Assert.Equal(["Details"], product.AspectKeys);

        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));
        Assert.Equal("Product", model.DefinitionId);
        Assert.Equal(1, model.DefinitionVersion);

        var section = Assert.Single(model.Sections);
        Assert.Equal("Details", section.AspectKey);

        // Display order first (Title, Price), then declaration order.
        Assert.Equal(
            ["Title", "Price", "Stock", "Published", "Tags", "Category", "ReleaseDate", "Spec", "Supplier", "Notes"],
            section.Fields.Select(static field => field.FacetId));

        var title = model.FindField("Details", "Title")!;
        Assert.Equal(("Title", AsterValueKind.String, true, "text"), (title.Label, title.Kind, title.IsRequired, title.UiHint));
        Assert.Equal("aspects.Details.Title", title.RequestPath);

        Assert.Equal(AsterValueKind.Number, model.FindField("Details", "Price")!.Kind);
        Assert.Equal(AsterValueKind.Integer, model.FindField("Details", "Stock")!.Kind);
        Assert.Equal(AsterValueKind.Boolean, model.FindField("Details", "Published")!.Kind);
        Assert.Equal(AsterValueKind.DateTime, model.FindField("Details", "ReleaseDate")!.Kind);
        Assert.Equal(AsterValueKind.Object, model.FindField("Details", "Spec")!.Kind);

        var tags = model.FindField("Details", "Tags")!;
        Assert.True(tags.IsArray);
        Assert.Equal(AsterValueKind.String, tags.Kind);

        var category = model.FindField("Details", "Category")!;
        Assert.Equal(["tools", "toys"], category.AllowedValues.Select(AsterFieldValues.ToText));

        var supplier = model.FindField("Details", "Supplier")!;
        Assert.Equal((AsterValueKind.Reference, false), (supplier.Kind, supplier.IsArray));
        Assert.Equal(["Supplier"], supplier.ReferenceTargets);

        Assert.Equal("Free text.", model.FindField("Details", "Notes")!.Description);
    }

    [Fact]
    public async Task FormForRuntimeDefinition_CreatesAndUpdatesResource()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.RegisterAsync(AsterTestServer.Product());
        await server.CreateAsync("Supplier", "acme", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme" } });

        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));
        var state = AsterFormState.Create(model);

        // Values as Elsa Studio's editors write them: text, a bool, a list, culture-formatted date text, a wrapped JSON literal.
        Set(state, "Title", "Hammer");
        Set(state, "Price", "12.5");
        Set(state, "Stock", "3");
        Set(state, "Published", true);
        Set(state, "Tags", new List<string> { "steel", "heavy" });
        Set(state, "Category", "tools");
        Set(state, "ReleaseDate", new DateTime(2026, 1, 2, 0, 0, 0).ToString());
        Set(state, "Spec", new global::Elsa.Api.Client.Shared.Models.WrappedInput
        {
            Expression = new global::Elsa.Api.Client.Resources.Scripting.Models.Expression("Literal", "{ \"weight\": 2 }"),
        });
        Set(state, "Supplier", "acme");
        Assert.Empty(state.InputErrors);

        var created = await client.CreateResourceAsync("Product", "hammer", state.BuildAspects());
        Assert.Equal(("hammer", 1), (created.ResourceId, created.Version));

        var stored = (await client.GetResourceAsync("hammer"))!.Aspects["Details"]!.AsObject();
        Assert.Equal("Hammer", stored["Title"]!.GetValue<string>());
        Assert.Equal(12.5m, stored["Price"]!.GetValue<decimal>());
        Assert.Equal(3, stored["Stock"]!.GetValue<int>());
        Assert.True(stored["Published"]!.GetValue<bool>());
        Assert.Equal(["steel", "heavy"], stored["Tags"]!.AsArray().Select(static tag => tag!.GetValue<string>()));
        Assert.Equal("2026-01-02T00:00:00Z", stored["ReleaseDate"]!.GetValue<string>());
        Assert.Equal(2, stored["Spec"]!["weight"]!.GetValue<int>());
        Assert.Equal("acme", stored["Supplier"]!.GetValue<string>());

        // Edit the stored version: the form loads its values and saves version 2 on top of version 1.
        var latest = (await client.GetResourceAsync("hammer"))!;
        var editing = AsterFormState.Create(model, latest.Aspects);
        Assert.Equal("Hammer", editing.GetEditorValue(model.FindField("Details", "Title")!, AsterEditorMapping.Resolve(model.FindField("Details", "Title")!)));
        Set(editing, "Title", "Claw hammer");
        var updated = await client.UpdateResourceAsync("hammer", latest.Version, editing.BuildAspects());
        Assert.Equal(2, updated.Version);
        Assert.Equal("Claw hammer", updated.Aspects["Details"]!["Title"]!.GetValue<string>());
        Assert.Equal(3, updated.Aspects["Details"]!["Stock"]!.GetValue<int>());

        static void Set(AsterFormState state, string facetId, object? value)
        {
            var field = state.Model.FindField("Details", facetId)!;
            Assert.True(state.SetEditorValue(field, AsterEditorMapping.Resolve(field), value), state.InputErrors.GetValueOrDefault(field.Key));
        }
    }

    [Fact]
    public void SchemaWithoutAsterExtensions_FallsBackToJsonTypes()
    {
        var schema = JsonNode.Parse("""
            {
              "title": "Plain",
              "properties": {
                "Main": {
                  "type": ["object", "null"],
                  "required": ["Name"],
                  "properties": {
                    "Name": { "type": "string" },
                    "When": { "type": ["string", "null"], "format": "date-time" },
                    "Id": { "type": "string", "format": "uuid" },
                    "Count": { "type": "integer" },
                    "Scores": { "type": "array", "items": { "type": "number" } },
                    "Anything": {}
                  }
                }
              }
            }
            """)!.AsObject();

        var model = AsterFormModel.FromSchema(schema);

        Assert.Equal(("Plain", (int?)null), (model.DefinitionId, model.DefinitionVersion));
        Assert.True(model.FindField("Main", "Name")!.IsRequired);
        Assert.Equal(AsterValueKind.DateTime, model.FindField("Main", "When")!.Kind);
        Assert.Equal(AsterValueKind.Guid, model.FindField("Main", "Id")!.Kind);
        Assert.Equal(AsterValueKind.Integer, model.FindField("Main", "Count")!.Kind);
        Assert.Equal((AsterValueKind.Number, true), (model.FindField("Main", "Scores")!.Kind, model.FindField("Main", "Scores")!.IsArray));
        Assert.Equal(AsterValueKind.Any, model.FindField("Main", "Anything")!.Kind);
    }

    [Fact]
    public void UpdatePayload_KeepsFacetsTheFormDoesNotKnow()
    {
        var model = AsterFormModel.FromSchema(JsonNode.Parse("""
            { "title": "T", "properties": { "A": { "type": "object", "properties": { "Known": { "type": "string", "x-aster-type": "string" } } } } }
            """)!.AsObject());
        var state = AsterFormState.Create(model, JsonNode.Parse("""{ "A": { "Known": "x", "Legacy": 5 }, "Other": { "Z": 1 } }""")!.AsObject());

        var aspects = state.BuildAspects();

        Assert.Equal(5, aspects["A"]!["Legacy"]!.GetValue<int>());
        Assert.Equal("x", aspects["A"]!["Known"]!.GetValue<string>());

        // Aspects outside the form are left out, so an update keeps them as they are.
        Assert.Null(aspects["Other"]);
    }

    [Fact]
    public void ReadResource_ReadsTheEndpointShape()
    {
        var resource = AsterStudioClient.ReadResource(JsonNode.Parse("""
            { "resourceId": "r", "definitionId": "D", "definitionVersion": 2, "version": 3, "created": "2026-01-02T03:04:05Z", "aspects": { "A": { "F": 1 } } }
            """)!.AsObject());

        Assert.Equal(("r", "D", (int?)2, 3), (resource.ResourceId, resource.DefinitionId, resource.DefinitionVersion, resource.Version));
        Assert.Equal(1, resource.Aspects["A"]!["F"]!.GetValue<int>());
    }
}
