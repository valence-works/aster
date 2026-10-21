using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Studio.Tests;

/// <summary>The module's HTTP client over the <c>Aster.AspNetCore</c> endpoints.</summary>
public sealed class AsterStudioClientTests
{
    private static JsonObject Supplier(string name) => new() { ["Info"] = new JsonObject { ["Name"] = name } };

    private static async Task<AsterTestServer> StartWithSuppliersAsync()
    {
        var server = await AsterTestServer.StartAsync();
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.RegisterAsync(AsterTestServer.Product());
        foreach (var id in new[] { "acme", "acme-tools", "globex", "initech" })
            await server.CreateAsync("Supplier", id, Supplier(id));
        return server;
    }

    [Fact]
    public async Task ReferencePickerSearch_QueriesTheTargetDefinitionsThroughTheQueryEndpoint()
    {
        await using var server = await StartWithSuppliersAsync();
        await server.CreateAsync("Product", "acme-hammer", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Hammer" } });
        var client = server.CreateClient();
        server.Recorder.Clear();

        var candidates = await AsterQueries.SearchReferenceCandidatesAsync(client, ["Supplier"], "acme");

        // Only suppliers, though a product's ID also contains "acme".
        Assert.Equal(["acme", "acme-tools"], candidates.Select(static candidate => candidate.ResourceId));
        Assert.All(candidates, static candidate => Assert.Equal("Supplier", candidate.DefinitionId));

        // GET /resources/query/capabilities picks the operator, then POST /resources/query searches the target definition.
        var requests = server.Recorder.Requests;
        Assert.Equal(("GET", "/api/aster/resources/query/capabilities"), (requests[0].Method, requests[0].PathAndQuery));
        var query = Assert.Single(requests, static request => request.PathAndQuery == "/api/aster/resources/query");
        Assert.Equal("POST", query.Method);
        Assert.Equal("Supplier", query.Body!["definitionId"]!.GetValue<string>());
        Assert.Equal("ResourceId", query.Body["filter"]!["field"]!.GetValue<string>());
        Assert.Equal("contains", query.Body["filter"]!["op"]!.GetValue<string>());
        Assert.Equal("acme", query.Body["filter"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReferencePickerSearch_WithoutTargets_SearchesEveryDefinition()
    {
        await using var server = await StartWithSuppliersAsync();
        await server.CreateAsync("Product", "globex-drill", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Drill" } });
        var client = server.CreateClient();

        var candidates = await AsterQueries.SearchReferenceCandidatesAsync(client, [], "globex");

        Assert.Equal(["globex", "globex-drill"], candidates.Select(static candidate => candidate.ResourceId));
        Assert.Null(server.Recorder.Requests.Last(static request => request.PathAndQuery == "/api/aster/resources/query").Body!["definitionId"]);
    }

    [Fact]
    public void Search_FallsBackWhenTheProviderCannotFilterIds()
    {
        var startsWithOnly = new AsterQueryCapabilities(new HashSet<string> { "equals", "startswith" }, new HashSet<string>(), SupportsMetadataSorting: false);
        Assert.Equal("startsWith", AsterQueries.Search("D", "ac", startsWithOnly, 5).Query["filter"]!["op"]!.GetValue<string>());

        var none = new AsterQueryCapabilities(new HashSet<string> { "equals" }, new HashSet<string>(), SupportsMetadataSorting: false);
        var (query, filtersText) = AsterQueries.Search("D", "ac", none, 5);
        Assert.False(filtersText);
        Assert.Null(query["filter"]);
        Assert.Null(query["sort"]);
    }

    [Fact]
    public async Task Referrers_UseTheReferencesFilterOnEachReferenceFacet()
    {
        await using var server = await StartWithSuppliersAsync();
        await server.CreateAsync("Product", "hammer", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Hammer", ["Supplier"] = "acme" } });
        await server.CreateAsync("Product", "saw", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Saw", ["Supplier"] = "globex" } });
        var client = server.CreateClient();
        server.Recorder.Clear();

        var referrers = await AsterQueries.FindReferrersAsync(client, "acme", "Supplier");

        var referrer = Assert.Single(referrers);
        Assert.Equal(("hammer", "Product", "Details.Supplier"), (referrer.ResourceId, referrer.DefinitionId, Assert.Single(referrer.Via)));

        var lookup = Assert.Single(server.Recorder.Requests, static request => request.PathAndQuery == "/api/aster/resources/query");
        Assert.Equal("Product", lookup.Body!["definitionId"]!.GetValue<string>());
        Assert.Equal(("Details", "Supplier", "acme"), (
            lookup.Body["filter"]!["aspect"]!.GetValue<string>(),
            lookup.Body["filter"]!["facet"]!.GetValue<string>(),
            lookup.Body["filter"]!["references"]!.GetValue<string>()));
    }

    [Fact]
    public async Task VersionHistory_ShowsChannelActivation()
    {
        await using var server = await StartWithSuppliersAsync();
        var client = server.CreateClient();
        await client.UpdateResourceAsync("acme", 1, Supplier("Acme v2"));

        await client.ActivateAsync("acme", 1, "Published", allowMultipleActive: false);
        await client.ActivateAsync("acme", 2, "Preview", allowMultipleActive: false);

        var versions = await client.GetVersionsAsync("acme");
        Assert.Equal([2, 1], versions.Select(static version => version.Version));
        Assert.True(versions[0].IsLatest);
        Assert.Equal(["Preview"], versions[0].ActiveChannels);
        Assert.Equal(["Published"], versions[1].ActiveChannels);

        // Roll back: activate version 2 in Published, which replaces version 1 there.
        await client.ActivateAsync("acme", 2, "Published", allowMultipleActive: false);
        await client.DeactivateAsync("acme", 2, "Preview");
        versions = await client.GetVersionsAsync("acme");
        Assert.Equal(["Published"], versions[0].ActiveChannels);
        Assert.Empty(versions[1].ActiveChannels);
        Assert.True(versions[1].IsDraft);

        var older = await client.GetResourceAsync("acme", 1);
        Assert.Equal("acme", older!.Aspects["Info"]!["Name"]!.GetValue<string>());
        Assert.Null(await client.GetResourceAsync("missing"));
    }

    [Fact]
    public async Task ActivationWithoutChannel_ReturnsADiagnostic()
    {
        await using var server = await StartWithSuppliersAsync();
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<AsterApiException>(() => client.ActivateAsync("acme", 1, " ", allowMultipleActive: false));

        Assert.Equal("channel-required", Assert.Single(exception.Problem.Diagnostics).Code);
    }

    [Fact]
    public async Task ListAndQuery_ReturnPages()
    {
        await using var server = await StartWithSuppliersAsync();
        var client = server.CreateClient();

        var capabilities = await client.GetQueryCapabilitiesAsync();
        Assert.NotNull(capabilities);
        Assert.Contains("contains", capabilities.MetadataOperators);
        Assert.Same(capabilities, await client.GetQueryCapabilitiesAsync());

        var (query, _) = AsterQueries.Search("Supplier", null, capabilities, take: 3);
        var page = await client.QueryResourcesAsync(query);
        Assert.Equal(["acme", "acme-tools", "globex"], page.Items.Select(static item => item.ResourceId));
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ErrorBodies_BecomeProblems()
    {
        await using var server = await AsterTestServer.StartAsync();
        var client = server.CreateClient();

        var notFound = await Assert.ThrowsAsync<AsterApiException>(() => client.GetDefinitionSchemaAsync("Nope"));
        Assert.Equal(404, notFound.Problem.Status);
        Assert.Contains("Nope", notFound.Problem.Summary);
    }

    [Fact]
    public async Task AddAsterStudioModule_RegistersTheModule()
    {
        var services = new ServiceCollection();
        services.AddAsterStudioModule(new Uri("https://elsa.example/api/aster"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Single(scope.ServiceProvider.GetServices<global::Elsa.Studio.Contracts.IFeature>());
        var menu = Assert.Single(scope.ServiceProvider.GetServices<global::Elsa.Studio.Contracts.IMenuProvider>());
        var item = Assert.Single(await menu.GetMenuItemsAsync());
        Assert.Equal("aster/definitions", item.Href);

        var handler = Assert.Single(scope.ServiceProvider.GetServices<global::Elsa.Studio.Contracts.IUIHintHandler>());
        Assert.True(handler.GetSupportsUIHint("aster-reference-picker"));

        // The base address gains the trailing slash relative routes need.
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AsterStudioClient));
        Assert.Equal("https://elsa.example/api/aster/", http.BaseAddress!.AbsoluteUri);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AsterStudioClient>());

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddAsterStudioModule(new Uri("/relative", UriKind.Relative)));
    }
}
