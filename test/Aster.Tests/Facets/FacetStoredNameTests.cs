using System.Text.Json.Serialization;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.SqliteJson;
using Aster.Tests.Persistence;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// #162: queries find a facet under its ID (or the camel-case form) in the stored JSON. A typed aspect whose
/// property is stored under another JSON name would pass validation yet never match a filter, so saving it is
/// rejected; a property whose JSON name is the facet ID is stored and found on every provider.
/// </summary>
public sealed class FacetStoredNameTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Postgres = "postgres";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");
    private readonly PostgresTestDatabase postgres = new();

    public void Dispose()
    {
        SqliteJsonTestDatabase.DeleteFiles(databasePath);
        postgres.Dispose();
    }

    private sealed record RenamedLink([property: JsonPropertyName("targetRef")] string Target);

    private sealed record ExplicitLink([property: JsonPropertyName("Target")] string Target);

    private sealed record CamelNamedLink([property: JsonPropertyName("target")] string Destination);

    private sealed record TypedLink([property: JsonPropertyName("targetRef")] string Target);

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task Save_PropertyStoredUnderAnotherJsonName_IsRejected(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, LinkDefinition("Target"));

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Page", Link(new RenamedLink("home"))));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.StoredNameMismatch, diagnostic.Code);
        Assert.Contains("'targetRef'", diagnostic.Message);
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task Save_JsonNameMatchingTheFacetId_IsStoredAndFound(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, LinkDefinition("Target"));
        await manager.CreateAsync("Page", Link(new ExplicitLink("home")));
        await manager.CreateAsync("Page", Link(new ExplicitLink("about")));

        Assert.Equal(["home"], await QueryTargetsAsync(provider, "Target", "home"));
        Assert.Equal(["about", "home"], await SortedTargetsAsync(provider, "Target"));
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task Save_JsonNameMatchingTheCamelFacetId_IsStoredAndFound(string providerKind)
    {
        // Queries find facet "Target" stored as "target", so a differently named property carrying that JSON name
        // holds the facet's value.
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider, LinkDefinition("Target"));
        await manager.CreateAsync("Page", Link(new CamelNamedLink("home")));

        Assert.Equal(["home"], await QueryTargetsAsync(provider, "Target", "home"));
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task Save_TypedAspectFacetNamedAfterItsJsonName_IsStoredAndFound(string providerKind)
    {
        // Facets derived from a typed aspect take the [JsonPropertyName] (ADR 0002 §8), so they always match.
        await using var provider = CreateProvider(providerKind);
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Page")
            .WithAspect<TypedLink>()
            .Build();
        var manager = await RegisterAsync(provider, definition);
        Assert.Equal("targetRef", Assert.Single(definition.AspectDefinitions[nameof(TypedLink)].FacetDefinitions).FacetDefinitionId);

        await manager.CreateAsync("Page", Link(new TypedLink("home"), nameof(TypedLink)));

        Assert.Equal(["home"], await QueryTargetsAsync(provider, "targetRef", "home", nameof(TypedLink)));
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task ReverseLookup_ReferenceStoredUnderItsFacetId_FindsTheReferrer(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(
            new ResourceDefinitionBuilder().WithDefinitionId("Home").WithAspect("Title").WithFacet("Name", "string").Build());
        var manager = await RegisterAsync(provider, new ResourceDefinitionBuilder()
            .WithDefinitionId("Page")
            .WithAspect("Link")
            .WithFacet("Target", "reference")
            .Build());
        var home = await manager.CreateAsync("Home", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Title"] = new Dictionary<string, object?> { ["Name"] = "Home" } },
        });
        var page = await manager.CreateAsync("Page", Link(new ExplicitLink(home.ResourceId)));

        var referrers = await provider.GetRequiredService<IResourceReferenceService>().GetReferrersAsync(home.ResourceId);

        var referrer = Assert.Single(referrers.Items);
        Assert.Equal(page.ResourceId, referrer.Resource.ResourceId);
        Assert.Equal([new ResourceReferenceLocation("Link", "Target", null)], referrer.Locations);
    }

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        else if (providerKind == Postgres)
            services.AddAsterPostgresJsonb(options => options.ConnectionString = postgres.ConnectionString);

        return services.BuildServiceProvider();
    }

    private static ResourceDefinition LinkDefinition(string facetId) =>
        new ResourceDefinitionBuilder()
            .WithDefinitionId("Page")
            .WithAspect("Link")
            .WithFacet(facetId, "string", isRequired: true)
            .Build();

    private static async Task<IResourceManager> RegisterAsync(ServiceProvider provider, ResourceDefinition definition)
    {
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition);
        return provider.GetRequiredService<IResourceManager>();
    }

    private static CreateResourceRequest Link(object aspect, string aspectKey = "Link") =>
        new() { InitialAspects = new Dictionary<string, object> { [aspectKey] = aspect } };

    private static async Task<List<string>> QueryTargetsAsync(IServiceProvider provider, string facetId, string value, string aspectKey = "Link") =>
        await TargetsAsync(provider, new ResourceQuery
        {
            DefinitionId = "Page",
            Filter = new FacetValueFilter(aspectKey, facetId, value, ComparisonOperator.Equals),
        }, aspectKey, facetId);

    private static async Task<List<string>> SortedTargetsAsync(IServiceProvider provider, string facetId) =>
        await TargetsAsync(provider, new ResourceQuery
        {
            DefinitionId = "Page",
            Sorts = [new SortExpression(facetId, AspectKey: "Link")],
        }, "Link", facetId);

    private static async Task<List<string>> TargetsAsync(IServiceProvider provider, ResourceQuery query, string aspectKey, string facetId)
    {
        var resources = await provider.GetRequiredService<IResourceQueryService>().QueryAsync(query);
        return resources
            .Select(resource => System.Text.Json.JsonSerializer.SerializeToElement(resource.Aspects[aspectKey]).EnumerateObject()
                .First(property => string.Equals(property.Name, facetId, StringComparison.OrdinalIgnoreCase)).Value.GetString()!)
            .ToList();
    }
}
