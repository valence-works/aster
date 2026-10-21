using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Elsa.Studio.Client;
using Aster.Elsa.Studio.Forms;
using Microsoft.Extensions.DependencyInjection;

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

        // Nothing changed: nothing to send.
        Assert.Empty(state.BuildAspects());

        var known = model.FindField("A", "Known")!;
        Assert.True(state.SetEditorValue(known, AsterEditorMapping.Resolve(known), "y"));
        var aspects = state.BuildAspects();

        Assert.Equal(5, aspects["A"]!["Legacy"]!.GetValue<int>());
        Assert.Equal("y", aspects["A"]!["Known"]!.GetValue<string>());

        // Aspects outside the form are left out, so an update keeps them as they are.
        Assert.Null(aspects["Other"]);
    }

    [Fact]
    public async Task ResourceSavedWithTypedAspect_LoadsAndSavesItsCamelCaseFacets()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(Catalogue());

        // The typed C# API stores the aspect as camel-case JSON ({"title": ...}); the server falls back to that key.
        var manager = server.Services.GetRequiredService<IResourceManager>();
        await manager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = "hammer",
            InitialAspects = new Dictionary<string, object> { ["Details"] = new TypedDetails { Title = "Hammer", Price = 5m, Stock = 3 } },
        });

        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));
        var latest = (await client.GetResourceAsync("hammer"))!;
        var state = AsterFormState.Create(model, latest.Aspects);
        Assert.Equal("Hammer", state.GetValue(model.FindField("Details", "Title")!)!.GetValue<string>());
        Assert.Equal(5m, state.GetValue(model.FindField("Details", "Price")!)!.GetValue<decimal>());

        var stock = model.FindField("Details", "Stock")!;
        Assert.True(state.SetEditorValue(stock, AsterEditorMapping.Resolve(stock), "4"));
        var details = state.BuildAspects()["Details"]!.AsObject();

        // One key per facet, the one the value was read from.
        Assert.Equal(4L, details["stock"]!.GetValue<long>());
        Assert.False(details.ContainsKey("Stock"));
        Assert.False(details.ContainsKey("Title"));

        await client.UpdateResourceAsync("hammer", latest.Version, state.BuildAspects());
        var cheap = await client.QueryResourcesAsync(new JsonObject
        {
            ["definitionId"] = "Product",
            ["filter"] = new JsonObject { ["aspect"] = "Details", ["facet"] = "Price", ["op"] = "equals", ["value"] = 5 },
        });
        Assert.Equal("hammer", Assert.Single(cheap.Items).ResourceId);

        var saved = AsterFormState.Create(model, (await client.GetResourceAsync("hammer"))!.Aspects);
        Assert.Equal("Hammer", saved.GetValue(model.FindField("Details", "Title")!)!.GetValue<string>());
        Assert.Equal(4, saved.GetValue(stock)!.GetValue<int>());
    }

    [Fact]
    public async Task AbsentAspectWithRequiredFacet_StaysAbsentOnCreateAndUpdate()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(Catalogue());
        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));
        var title = model.FindField("Details", "Title")!;

        // Seo.Slug is required, but only of a Seo aspect the resource has.
        var creating = AsterFormState.Create(model);
        Assert.True(creating.SetEditorValue(title, AsterEditorMapping.Resolve(title), "Hammer"));
        Assert.Null(creating.BuildAspects()["Seo"]);
        var created = await client.CreateResourceAsync("Product", "hammer", creating.BuildAspects());

        var editing = AsterFormState.Create(model, created.Aspects);
        Assert.True(editing.SetEditorValue(title, AsterEditorMapping.Resolve(title), "Claw hammer"));
        Assert.Null(editing.BuildAspects()["Seo"]);
        var updated = await client.UpdateResourceAsync("hammer", created.Version, editing.BuildAspects());

        Assert.Equal("Claw hammer", updated.Aspects["Details"]!["Title"]!.GetValue<string>());
        Assert.Null(updated.Aspects["Seo"]);
    }

    [Fact]
    public async Task UpdateOfOneAspect_DoesNotRevalidateReferencesInAnother()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.RegisterAsync(Catalogue());
        await server.CreateAsync("Supplier", "acme", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme" } });
        await client.ActivateAsync("acme", 1, "Published", false);
        await server.CreateAsync("Product", "hammer", new JsonObject
        {
            ["Details"] = new JsonObject { ["Title"] = "Hammer" },
            ["Seo"] = new JsonObject { ["Slug"] = "hammer" },
            ["Links"] = new JsonObject { ["Supplier"] = "acme" },
        });

        // The supplier leaves Published after the product pinned it there.
        await client.DeactivateAsync("acme", 1, "Published");

        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));
        var latest = (await client.GetResourceAsync("hammer"))!;
        var state = AsterFormState.Create(model, latest.Aspects);
        var title = model.FindField("Details", "Title")!;
        Assert.True(state.SetEditorValue(title, AsterEditorMapping.Resolve(title), "Claw hammer"));
        Assert.Null(state.BuildAspects()["Links"]);

        var updated = await client.UpdateResourceAsync("hammer", latest.Version, state.BuildAspects());
        Assert.Equal("Claw hammer", updated.Aspects["Details"]!["Title"]!.GetValue<string>());
        Assert.Equal("acme", updated.Aspects["Links"]!["Supplier"]!.GetValue<string>());
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

    // A product with an optional aspect that has a required facet, and a reference pinned to Published.
    private static ResourceDefinition Catalogue() => new ResourceDefinitionBuilder()
        .WithDefinitionId("Product")
        .WithAspect("Details")
            .WithFacet("Title", "string")
            .WithFacet("Price", "decimal")
            .WithFacet("Stock", "int")
        .WithAspect("Seo")
            .WithFacet("Slug", "string", isRequired: true)
        .WithAspect("Links")
            .WithFacet("Supplier", "reference")
            .WithFacetMetadata(new FacetMetadata { ReferenceResolution = "Published", TargetDefinitionIds = ["Supplier"] })
        .Build();

    // Saved through the typed C# API, which stores it in camel case.
    private sealed class TypedDetails
    {
        public string? Title { get; set; }

        public decimal? Price { get; set; }

        public int? Stock { get; set; }
    }
}
