using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.AspNetCore;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Elsa.Studio.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aster.Elsa.Studio.Tests;

/// <summary>
/// An in-process Aster server: <c>Aster.AspNetCore</c>'s endpoints mapped at <c>/api/aster</c> over the in-memory
/// provider, as an Elsa server hosting Aster would map them. Every request the module sends is recorded.
/// </summary>
internal sealed class AsterTestServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly WebApplication app;

    private AsterTestServer(WebApplication app, RecordingHandler recorder)
    {
        this.app = app;
        Recorder = recorder;
    }

    /// <summary>Gets the requests the module's client sent, in order.</summary>
    public RecordingHandler Recorder { get; }

    /// <summary>Gets the server's services, for writing through the typed C# API.</summary>
    public IServiceProvider Services => app.Services;

    /// <summary>The base address the module is configured with.</summary>
    public static Uri BaseAddress { get; } = new("http://localhost/api/aster/");

    public static async Task<AsterTestServer> StartAsync(Action<AsterEndpointOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAsterCore();

        var app = builder.Build();
        app.MapAster("/api/aster", configure);
        await app.StartAsync();

        return new AsterTestServer(app, new RecordingHandler(app.GetTestServer().CreateHandler()));
    }

    /// <summary>Creates an HTTP client for the module, as <c>AddAsterStudioModule</c> configures it.</summary>
    public HttpClient CreateHttpClient() => new(Recorder, disposeHandler: false) { BaseAddress = BaseAddress };

    /// <summary>Creates the module's client.</summary>
    public AsterStudioClient CreateClient() => new(CreateHttpClient());

    /// <summary>Registers a definition over HTTP, as an administrator would at runtime.</summary>
    public async Task RegisterAsync(ResourceDefinition definition)
    {
        using var client = CreateHttpClient();
        using var response = await client.PostAsJsonAsync("definitions", definition, Web);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Creates a resource over HTTP.</summary>
    public async Task CreateAsync(string definitionId, string resourceId, JsonObject aspects)
    {
        using var client = CreateHttpClient();
        using var response = await client.PostAsJsonAsync("resources", new JsonObject
        {
            ["definitionId"] = definitionId,
            ["resourceId"] = resourceId,
            ["aspects"] = aspects,
        }, Web);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        Recorder.Dispose();
        await app.DisposeAsync();
    }

    /// <summary>
    /// A product type registered at runtime, with a facet for every standard UI hint the tests exercise and one with
    /// a hint the module doesn't know.
    /// </summary>
    public static ResourceDefinition Product() => new ResourceDefinitionBuilder()
        .WithDefinitionId("Product")
        .WithAspect("Details")
            .WithFacet("Title", "string", isRequired: true)
            .WithFacetMetadata(new FacetMetadata { Label = "Title", MaxLength = 20, UiHint = "text", DisplayOrder = 1 })
            .WithFacet("Price", "decimal")
            .WithFacetMetadata(new FacetMetadata { Label = "Price", Minimum = 0, UiHint = "number", DisplayOrder = 2 })
            .WithFacet("Stock", "int")
            .WithFacet("Published", "bool")
            .WithFacetMetadata(new FacetMetadata { UiHint = "switch" })
            .WithFacet("Tags", "string[]")
            .WithFacetMetadata(new FacetMetadata { UiHint = "tags", MaxLength = 10 })
            .WithFacet("Category", "string")
            .WithFacetMetadata(new FacetMetadata { UiHint = "select", AllowedValues = ["tools", "toys"] })
            .WithFacet("ReleaseDate", "datetime")
            .WithFacetMetadata(new FacetMetadata { UiHint = "date" })
            .WithFacet("Spec", "object")
            .WithFacetMetadata(new FacetMetadata { UiHint = "json" })
            .WithFacet("Supplier", "reference")
            .WithFacetMetadata(new FacetMetadata { UiHint = "reference-picker", TargetDefinitionIds = ["Supplier"] })
            .WithFacet("Notes", "string")
            .WithFacetMetadata(new FacetMetadata { UiHint = "acme:fancy-editor", Description = "Free text." })
        .Build();

    /// <summary>The target of <c>Product.Details.Supplier</c>.</summary>
    public static ResourceDefinition Supplier() => new ResourceDefinitionBuilder()
        .WithDefinitionId("Supplier")
        .WithAspect("Info")
            .WithFacet("Name", "string", isRequired: true)
        .Build();
}

/// <summary>Forwards requests to the test server and records each one with its body.</summary>
internal sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly ConcurrentQueue<RecordedRequest> requests = new();

    public IReadOnlyList<RecordedRequest> Requests => requests.ToList();

    public void Clear() => requests.Clear();

    /// <summary>When set, the next request whose path and query matches waits for it before it is sent.</summary>
    public (string PathAndQuery, Task Release)? Gate { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        requests.Enqueue(new RecordedRequest(request.Method.Method, request.RequestUri!.PathAndQuery, body is null ? null : JsonNode.Parse(body)));
        if (Gate is { } gate && request.RequestUri.PathAndQuery == gate.PathAndQuery)
        {
            Gate = null;
            await gate.Release;
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>One request the module sent.</summary>
internal sealed record RecordedRequest(string Method, string PathAndQuery, JsonNode? Body);
