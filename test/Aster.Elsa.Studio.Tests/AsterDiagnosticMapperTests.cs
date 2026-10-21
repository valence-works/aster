using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Client;
using Aster.Elsa.Studio.Forms;

namespace Aster.Elsa.Studio.Tests;

/// <summary>#117: server diagnostics map to fields, by request-body path.</summary>
public sealed class AsterDiagnosticMapperTests
{
    [Fact]
    public async Task SaveDiagnostics_MapToTheirFacetFields()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(AsterTestServer.Product());

        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));
        var state = AsterFormState.Create(model);
        var title = model.FindField("Details", "Title")!;
        var tags = model.FindField("Details", "Tags")!;
        var category = model.FindField("Details", "Category")!;
        state.SetEditorValue(title, AsterEditorMapping.Resolve(title), "A title far longer than twenty characters");
        state.SetEditorValue(tags, AsterEditorMapping.Resolve(tags), new List<string> { "ok", "much-too-long-tag" });
        state.SetEditorValue(category, AsterEditorMapping.Resolve(category), "food");

        var exception = await Assert.ThrowsAsync<AsterApiException>(() => client.CreateResourceAsync("Product", "bad", state.BuildAspects()));
        Assert.Equal(400, exception.Problem.Status);

        var errors = AsterDiagnosticMapper.Map(exception.Problem.Diagnostics, AsterDiagnosticMapper.ResourceTargets(model));

        Assert.Empty(errors.Unplaced);
        Assert.Single(errors.For(title.Key));
        Assert.Single(errors.For(category.Key));

        // The element index names the offending tag.
        Assert.StartsWith("Item 2: ", Assert.Single(errors.For(tags.Key)));
        Assert.Empty(errors.For(model.FindField("Details", "Price")!.Key));
    }

    [Fact]
    public async Task MissingRequiredFacet_MapsToItsField()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(AsterTestServer.Product());
        var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync("Product"));

        var exception = await Assert.ThrowsAsync<AsterApiException>(() => client.CreateResourceAsync("Product", null, AsterFormState.Create(model).BuildAspects()));
        var diagnostic = Assert.Single(exception.Problem.Diagnostics);
        Assert.Equal(("Details", "Title"), (diagnostic.AspectKey, diagnostic.FacetId));
        Assert.Equal("aspects.Details.Title", AsterDiagnosticMapper.PathOf(diagnostic));

        var errors = AsterDiagnosticMapper.Map(exception.Problem.Diagnostics, AsterDiagnosticMapper.ResourceTargets(model));
        Assert.Single(errors.For("Details.Title"));
    }

    [Fact]
    public async Task QueryDiagnostics_MapToTheInputsTheirPathsName()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();

        // POST /resources/query/validate (#194) returns every failure with a request-body path.
        var validation = await client.ValidateQueryAsync(new JsonObject
        {
            ["lifecycle"] = "sometimes",
            ["filter"] = new JsonObject { ["field"] = "ResourceId", ["op"] = "resembles", ["value"] = "x" },
        });
        Assert.False(validation.IsValid);
        var diagnostic = Assert.Single(validation.Diagnostics);
        Assert.Equal("lifecycle", diagnostic.Path);

        var operatorValidation = await client.ValidateQueryAsync(new JsonObject
        {
            ["filter"] = new JsonObject { ["and"] = new JsonArray(new JsonObject { ["field"] = "ResourceId", ["op"] = "resembles", ["value"] = "x" }) },
        });
        var operatorDiagnostic = Assert.Single(operatorValidation.Diagnostics);
        Assert.StartsWith("filter.and[0]", operatorDiagnostic.Path);

        AsterErrorTarget[] targets = [new("search", "filter"), new("lifecycle", "lifecycle"), new("paging", "take")];
        var errors = AsterDiagnosticMapper.Map([.. validation.Diagnostics, .. operatorValidation.Diagnostics], targets);
        Assert.Single(errors.For("lifecycle"));
        Assert.Single(errors.For("search"));
        Assert.Empty(errors.Unplaced);
    }

    [Fact]
    public void Paths_MatchTheLongestTargetAtABoundary()
    {
        AsterErrorTarget[] targets =
        [
            new("aspect", "aspects.Content"),
            new("title", "aspects.Content.Title"),
            new("titles", "aspects.Content.Titles"),
            new("filter", "filter"),
        ];

        var errors = AsterDiagnosticMapper.Map(
        [
            new AsterDiagnostic("c1", "title", "Content", "Title"),
            new AsterDiagnostic("c2", "titles", "Content", "Titles", ElementIndex: 0),
            new AsterDiagnostic("c3", "unknown facet", "Content", "Other"),
            new AsterDiagnostic("c4", "aspect", "Content"),
            new AsterDiagnostic("c5", "op", Path: "filter.or[2].op"),
            new AsterDiagnostic("c6", "filters", Path: "filters"),
            new AsterDiagnostic("c7", "whole request"),
        ], targets);

        Assert.Equal(["title"], errors.For("title"));
        Assert.Equal(["Item 1: titles"], errors.For("titles"));
        Assert.Equal(["unknown facet", "aspect"], errors.For("aspect"));
        Assert.Equal(["op"], errors.For("filter"));

        // "filters" is not under "filter"; neither it nor a pathless diagnostic has a field.
        Assert.Equal(["filters (c6)", "whole request (c7)"], errors.Unplaced);
    }

    [Fact]
    public async Task ConcurrencyConflict_IsAProblemWithItsCode()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.CreateAsync("Supplier", "acme", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme" } });
        await client.UpdateResourceAsync("acme", 1, new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme 2" } });

        var exception = await Assert.ThrowsAsync<AsterApiException>(() =>
            client.UpdateResourceAsync("acme", 1, new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Stale" } }));

        Assert.Equal((409, "concurrency-conflict"), (exception.Problem.Status, exception.Problem.Code));
    }
}
