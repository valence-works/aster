using System.Text.Json.Serialization;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #124: typed aspects derive facet definitions from their properties (ADR 0002 §8), and
/// untyped aspects declare facets explicitly with WithAspect(string) and WithFacet.
/// </summary>
public sealed class TypedAspectFacetDerivationTests
{
    private enum Status
    {
        Draft,
        Live,
    }

    private sealed record Nested(string Value);

    private readonly record struct Point(int X, int Y);

    private sealed class EveryShape
    {
        public required string Title { get; init; }

        public string? Subtitle { get; init; }

        public int Count { get; init; }

        public long Total { get; init; }

        public short Small { get; init; }

        public int? MaybeCount { get; init; }

        public decimal Price { get; init; }

        public double Ratio { get; init; }

        public bool Enabled { get; init; }

        public DateTime PublishedAt { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public Guid Key { get; init; }

        public List<string> Tags { get; init; } = [];

        public int[] Scores { get; init; } = [];

        public IReadOnlyList<Guid> Owners { get; init; } = [];

        public ISet<DateTime> Dates { get; init; } = new HashSet<DateTime>();

        public Nested? Details { get; init; }

        public Dictionary<string, string> Map { get; init; } = [];

        public Point Position { get; init; }

        // Not derived: object[] is outside the grammar, enums are decided in #131, nullable elements
        // would be rejected by an array facet, byte[] and TimeSpan serialize as strings, and an
        // object-typed property has no known JSON shape.

        public List<Nested> Children { get; init; } = [];

        public List<Status> States { get; init; } = [];

        public List<int?> Sparse { get; init; } = [];

        public byte[] Blob { get; init; } = [];

        public TimeSpan Duration { get; init; }

        public object? Anything { get; init; }

        public Uri? Link { get; init; }

        public Status State { get; init; }

        [JsonIgnore]
        public string Ignored { get; init; } = string.Empty;

        public static string StaticValue => "static";

        public string this[int index] => index.ToString();
    }

    private sealed class Article
    {
        public required string Title { get; init; }

        public int Views { get; init; }
    }

    private static IReadOnlyList<FacetDefinition> Facets(ResourceDefinition definition, string aspectKey) =>
        definition.AspectDefinitions[aspectKey].FacetDefinitions;

    [Fact]
    public void WithAspectOfT_DerivesSupportedPropertiesInDeclarationOrder()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Shape")
            .WithAspect<EveryShape>()
            .Build();

        Assert.Equal(
            [
                ("Title", "string", true),
                ("Subtitle", "string", false),
                ("Count", "int", false),
                ("Total", "int", false),
                ("Small", "int", false),
                ("MaybeCount", "int", false),
                ("Price", "decimal", false),
                ("Ratio", "decimal", false),
                ("Enabled", "bool", false),
                ("PublishedAt", "datetime", false),
                ("ExpiresAt", "datetime", false),
                ("Key", "guid", false),
                ("Tags", "string[]", false),
                ("Scores", "int[]", false),
                ("Owners", "guid[]", false),
                ("Dates", "datetime[]", false),
                ("Details", "object", false),
                ("Map", "object", false),
                ("Position", "object", false),
            ],
            Facets(definition, "EveryShape").Select(static facet => (facet.FacetDefinitionId, facet.Type, facet.IsRequired)));
    }

    [Fact]
    public void WithNamedAspectOfT_DerivesFacetsForTheNamedAttachment()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Page")
            .WithNamedAspect<Article>("Primary")
            .Build();

        var aspect = definition.AspectDefinitions["Article:Primary"];
        Assert.True(aspect.RequiresName);
        Assert.Equal(["Title", "Views"], aspect.FacetDefinitions.Select(static facet => facet.FacetDefinitionId));
    }

    [Fact]
    public async Task DerivedFacets_AreEnforcedOnSave()
    {
        await using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(
            new ResourceDefinitionBuilder().WithDefinitionId("Article").WithAspect<Article>().Build());
        var manager = provider.GetRequiredService<IResourceManager>();

        var saved = await manager.CreateAsync("Article", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Article"] = new Article { Title = "Hello", Views = 3 } },
        });
        Assert.Equal(1, saved.Version);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Article", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["Article"] = new Dictionary<string, object> { ["Views"] = "many" },
                },
            }));
        Assert.Equal(
            [("Title", FacetValueDiagnosticCodes.Required), ("Views", FacetValueDiagnosticCodes.TypeMismatch)],
            exception.Diagnostics.Select(static diagnostic => (diagnostic.FacetDefinitionId, diagnostic.Code)));
    }

    [Fact]
    public void WithAspectAndWithFacet_DeclareUntypedAspects()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Agent")
            .WithAspect("Prompt")
            .WithFacet("Template", "string", isRequired: true)
            .WithFacetMetadata(new FacetMetadata { UiHint = "multiline" })
            .WithFacet("MaxTokens", "int")
            .WithNamedAspect("Price", "Sale")
            .WithFacet("Amount", "decimal")
            .Build();

        var prompt = Facets(definition, "Prompt");
        Assert.Equal([("Template", "string", true), ("MaxTokens", "int", false)], prompt.Select(static facet => (facet.FacetDefinitionId, facet.Type, facet.IsRequired)));
        Assert.Equal("multiline", prompt[0].Metadata?.UiHint);
        Assert.True(definition.AspectDefinitions["Price:Sale"].RequiresName);
        Assert.Equal("Amount", Assert.Single(Facets(definition, "Price:Sale")).FacetDefinitionId);
    }

    [Fact]
    public void WithFacet_DuplicateFacetId_Throws()
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Article").WithAspect<Article>();

        Assert.Throws<InvalidOperationException>(() => builder.WithFacet("Title", "string"));
    }

    [Fact]
    public void WithFacet_NoAspect_Throws()
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Article");

        Assert.Throws<InvalidOperationException>(() => builder.WithFacet("Title", "string"));
    }

    [Fact]
    public void WithFacetMetadataById_DecoratesDerivedFacet()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Article")
            .WithAspect<Article>()
            .WithFacetMetadata("Title", new FacetMetadata { MaxLength = 80 })
            .Build();

        var facets = Facets(definition, "Article");
        Assert.Equal(80, facets.Single(static facet => facet.FacetDefinitionId == "Title").Metadata?.MaxLength);
        Assert.Null(facets.Single(static facet => facet.FacetDefinitionId == "Views").Metadata);
    }

    [Fact]
    public void WithFacetMetadataById_UnknownFacet_Throws()
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Article").WithAspect<Article>();

        Assert.Throws<InvalidOperationException>(() => builder.WithFacetMetadata("Missing", new FacetMetadata()));
    }

#pragma warning disable CS0618 // Covers the obsolete API's compatibility behaviour.
    [Fact]
    public void WithTypedFacet_ReplacesDerivedFacetWithSameIdAndAppendsIt()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Count")
            .WithAspect<CountOnly>()
            .WithTypedFacet<int>()
            .Build();

        var facet = Assert.Single(Facets(definition, "CountOnly"));
        Assert.Equal(("Int32", "int32"), (facet.FacetDefinitionId, facet.Type));
    }
#pragma warning restore CS0618

    private sealed class CountOnly
    {
        public int Int32 { get; init; }
    }

    private sealed class Headed
    {
        [JsonPropertyName("headline")]
        public required string Title { get; init; }
    }

    [Fact]
    public void WithAspectOfT_JsonPropertyName_BecomesTheFacetId()
    {
        var definition = new ResourceDefinitionBuilder().WithDefinitionId("Story").WithAspect<Headed>().Build();

        Assert.Equal("headline", Assert.Single(Facets(definition, "Headed")).FacetDefinitionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonPropertyName_FacetValidatesFromPocoAndStoredJsonAndIsQueryable(bool sqlite)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddAsterCore();
            if (sqlite)
                services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId("Story")
                .WithAspect<Headed>()
                .WithAspect("Notes")
                .WithFacet("Text", "string")
                .Build());
            var manager = provider.GetRequiredService<IResourceManager>();

            var created = await manager.CreateAsync("Story", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object> { ["Headed"] = new Headed { Title = "Launch" } },
            });

            // The update carries the stored Headed aspect forward and revalidates it.
            var updated = await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = created.Version,
                AspectUpdates = new Dictionary<string, object> { ["Notes"] = new Dictionary<string, object> { ["Text"] = "draft" } },
            });
            Assert.Equal(2, updated.Version);

            var filter = TypedQuery.For<Headed>().Facet(static aspect => aspect.Title).EqualTo("launch");
            var results = await provider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery { DefinitionId = "Story", Filter = filter });
            Assert.Equal(created.ResourceId, Assert.Single(results).ResourceId);
        }
        finally
        {
            SqliteJsonTestDatabase.DeleteFiles(databasePath);
        }
    }
}
