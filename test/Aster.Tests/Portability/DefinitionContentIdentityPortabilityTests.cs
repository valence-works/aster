using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

/// <summary>
/// #221: definitions match by <c>(DefinitionId, Version)</c> and compare by content, not by the IDs
/// <see cref="ResourceDefinitionBuilder"/> generates for the definition, its aspects and its facets. Two
/// environments that register the same definitions from the same code import from each other without
/// remapping, in either collision mode and across providers; definitions whose content differs still diverge.
/// </summary>
public sealed class DefinitionContentIdentityPortabilityTests
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Postgres = "postgres";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A typed aspect: its facets are derived, each with a generated ID.</summary>
    private sealed record Biography
    {
        public string Text { get; init; } = string.Empty;

        public string[] Topics { get; init; } = [];
    }

    private static ResourceDefinition AuthorDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId("Author")
        .WithAspect("Profile")
        .WithFacet("Name", "string", isRequired: true)
        .WithFacetMetadata(new FacetMetadata { MinLength = 1 })
        .WithAspect<Biography>()
        .Build();

    private static ResourceDefinitionBuilder ArticleBuilder(bool titleRequired = true) => new ResourceDefinitionBuilder()
        .WithDefinitionId("Article")
        .WithAspect("Body")
        .WithFacet("Title", "string", isRequired: titleRequired)
        .WithFacet("Author", "reference")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Author"] });

    /// <summary>The same definitions every environment registers from code, each call with new generated IDs.</summary>
    private static async Task RegisterAsync(Environment environment, ResourceDefinition? article = null)
    {
        var definitions = environment.Services.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(AuthorDefinition());
        await definitions.RegisterDefinitionAsync(article ?? ArticleBuilder().Build());
    }

    /// <summary>Article variants whose content differs from <see cref="ArticleBuilder"/>'s.</summary>
    private static ResourceDefinition DivergentArticle(string variant) => variant switch
    {
        "optional-facet" => ArticleBuilder(titleRequired: false).Build(),
        "extra-facet" => ArticleBuilder().WithFacet("Subtitle", "string").Build(),
        "facet-metadata" => ArticleBuilder().WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Author", "Article"] }).Build(),
        "extra-aspect" => ArticleBuilder().WithAspect("Notes").Build(),
        "singleton" => ArticleBuilder().WithSingleton().Build(),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    /// <summary>author-1, and article-1 referencing it and active in Published; exported and sent as JSON.</summary>
    private static async Task<PortableSnapshot> SeedAndExportAsync(Environment source)
    {
        var manager = source.Services.GetRequiredService<IResourceManager>();
        await manager.CreateAsync("Author", new CreateResourceRequest
        {
            ResourceId = "author-1",
            InitialAspects = new Dictionary<string, object>
            {
                ["Profile"] = new Dictionary<string, object> { ["Name"] = "Ada" },
                [nameof(Biography)] = new Biography { Text = "Wrote the first program.", Topics = ["engines"] },
            },
        });
        var article = await manager.CreateAsync("Article", new CreateResourceRequest
        {
            ResourceId = "article-1",
            InitialAspects = new Dictionary<string, object>
            {
                ["Body"] = new Dictionary<string, object> { ["Title"] = "Notes", ["Author"] = "author-1" },
            },
        });
        await manager.ActivateAsync(article.ResourceId, article.Version, "Published");

        var exported = await source.Services.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.DefinitionWithResources,
            DefinitionIds = ["Author", "Article"],
        });
        Assert.Empty(exported.Diagnostics);

        return JsonSerializer.Deserialize<PortableSnapshot>(JsonSerializer.Serialize(exported.Snapshot, JsonOptions), JsonOptions)!;
    }

    private static async Task<ResourceDefinition> DefinitionAsync(Environment environment, string definitionId) =>
        (await environment.Services.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync(definitionId))!;

    [SkippableTheory]
    [InlineData(InMemory, Sqlite, PortableImportCollisionMode.Strict)]
    [InlineData(InMemory, Sqlite, PortableImportCollisionMode.RemapDivergent)]
    [InlineData(Sqlite, InMemory, PortableImportCollisionMode.Strict)]
    [InlineData(Sqlite, InMemory, PortableImportCollisionMode.RemapDivergent)]
    [InlineData(Sqlite, Sqlite, PortableImportCollisionMode.Strict)]
    [InlineData(InMemory, InMemory, PortableImportCollisionMode.RemapDivergent)]
    [InlineData(InMemory, Postgres, PortableImportCollisionMode.Strict)]
    [InlineData(Postgres, Sqlite, PortableImportCollisionMode.RemapDivergent)]
    public async Task SameDefinitionsRegisteredInTwoEnvironments_AreReusedWithTheTargetsIds(
        string sourceKind,
        string targetKind,
        PortableImportCollisionMode mode)
    {
        await using var source = Environment.Create(sourceKind);
        await using var target = Environment.Create(targetKind);
        await RegisterAsync(source);
        await RegisterAsync(target);
        var targetArticle = await DefinitionAsync(target, "Article");
        var targetAuthor = await DefinitionAsync(target, "Author");
        var snapshot = await SeedAndExportAsync(source);

        // Each registration generated its own IDs; only those differ.
        var exportedArticle = Assert.Single(snapshot.Definitions, static definition => definition.DefinitionId == "Article");
        Assert.NotEqual(targetArticle.Id, exportedArticle.Id);
        Assert.NotEqual(targetArticle.AspectDefinitions["Body"].Id, exportedArticle.AspectDefinitions["Body"].Id);
        Assert.NotEqual(targetArticle.AspectDefinitions["Body"].FacetDefinitions[0].Id, exportedArticle.AspectDefinitions["Body"].FacetDefinitions[0].Id);

        var portability = target.Services.GetRequiredService<IResourcePortabilityService>();
        var options = new PortableImportOptions { CollisionMode = mode };
        var preview = await portability.PreviewImportAsync(snapshot, options);

        Assert.True(preview.CanImport);
        Assert.Empty(preview.Diagnostics);
        Assert.Equal((0, 0), (preview.Counts.Definitions, preview.Counts.RemappedItems));
        Assert.Equal(
            new[] { """["Article",1]""", """["Author",1]""" },
            preview.IdentityMap
                .Where(static mapping => mapping.EntityKind == PortableEntityKind.DefinitionVersion)
                .Select(static mapping =>
                {
                    Assert.Equal(PortableIdentityMappingReason.ReusedIdentical, mapping.Reason);
                    Assert.Equal(mapping.SourceId, mapping.TargetId);
                    return mapping.SourceId;
                })
                .Order(StringComparer.Ordinal));

        var result = await portability.ImportAsync(snapshot, options);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal((0, 2, 1, 0), (result.Counts.Definitions, result.Counts.ResourceVersions, result.Counts.ActivationEntries, result.Counts.RemappedItems));
        Assert.DoesNotContain(result.IdentityMap, static mapping => mapping.Reason != PortableIdentityMappingReason.Preserved
            && mapping.Reason != PortableIdentityMappingReason.ReusedIdentical);

        // The target keeps its own copies: no new definition or version, its IDs unchanged.
        var definitions = (await target.Services.GetRequiredService<IResourceDefinitionStore>().ListDefinitionsAsync()).ToList();
        Assert.Equal(new[] { "Article", "Author" }, definitions.Select(static definition => definition.DefinitionId).Order(StringComparer.Ordinal));
        Assert.Equal((targetArticle.Id, 1), ((await DefinitionAsync(target, "Article")).Id, (await DefinitionAsync(target, "Article")).Version));
        Assert.Equal(targetAuthor.Id, (await DefinitionAsync(target, "Author")).Id);

        // Imported resource versions are bound to the target's definition version.
        var manager = target.Services.GetRequiredService<IResourceManager>();
        foreach (var (resourceId, definitionId) in new[] { ("author-1", "Author"), ("article-1", "Article") })
        {
            var imported = await manager.GetLatestVersionAsync(resourceId);
            Assert.NotNull(imported);
            Assert.Equal((definitionId, (int?)1), (imported.DefinitionId, imported.DefinitionVersion));
        }

        Assert.Equal(1, Assert.Single(await manager.GetActiveVersionsAsync("article-1", "Published")).Version);

        // The target still accepts new resources against its own definition.
        await manager.CreateAsync("Article", new CreateResourceRequest
        {
            ResourceId = "article-2",
            InitialAspects = new Dictionary<string, object>
            {
                ["Body"] = new Dictionary<string, object> { ["Title"] = "More", ["Author"] = "author-1" },
            },
        });

        var again = await portability.ImportAsync(snapshot, options);
        Assert.Equal(PortableImportStatus.NoOp, again.Status);
    }

    [SkippableTheory]
    [InlineData(InMemory, Sqlite, "optional-facet")]
    [InlineData(Sqlite, InMemory, "extra-facet")]
    [InlineData(InMemory, Sqlite, "facet-metadata")]
    [InlineData(Sqlite, InMemory, "extra-aspect")]
    [InlineData(InMemory, InMemory, "singleton")]
    [InlineData(Postgres, InMemory, "extra-facet")]
    public async Task DefinitionWhoseContentDiffers_StillDiverges(string sourceKind, string targetKind, string variant)
    {
        await using var source = Environment.Create(sourceKind);
        await RegisterAsync(source);
        var snapshot = await SeedAndExportAsync(source);

        // Strict: the divergent Article fails the import; the identical Author would be reused.
        await using (var target = Environment.Create(targetKind))
        {
            await RegisterAsync(target, DivergentArticle(variant));

            var result = await target.Services.GetRequiredService<IResourcePortabilityService>().ImportAsync(
                snapshot,
                new PortableImportOptions { CollisionMode = PortableImportCollisionMode.Strict });

            Assert.Equal(PortableImportStatus.Failed, result.Status);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal((PortableDiagnosticCodes.DivergentIdentityCollision, "definitions/Article/1"), (diagnostic.Code, diagnostic.Path));
            Assert.Contains(result.IdentityMap, static mapping => mapping is
            {
                EntityKind: PortableEntityKind.DefinitionVersion,
                SourceId: """["Article",1]""",
                Reason: PortableIdentityMappingReason.CollidedDivergent,
            });
            Assert.Contains(result.IdentityMap, static mapping => mapping is
            {
                EntityKind: PortableEntityKind.DefinitionVersion,
                SourceId: """["Author",1]""",
                Reason: PortableIdentityMappingReason.ReusedIdentical,
            });
            Assert.Null(await target.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("article-1"));
        }

        // RemapDivergent: the divergent Article moves to a new definition, which its resources follow.
        await using (var target = Environment.Create(targetKind))
        {
            await RegisterAsync(target, DivergentArticle(variant));

            var result = await target.Services.GetRequiredService<IResourcePortabilityService>().ImportAsync(
                snapshot,
                new PortableImportOptions { CollisionMode = PortableImportCollisionMode.RemapDivergent });

            Assert.Equal(PortableImportStatus.Imported, result.Status);
            var remapped = Assert.Single(result.IdentityMap, static mapping => mapping.Reason == PortableIdentityMappingReason.RemappedDivergent);
            Assert.Equal(
                (PortableEntityKind.DefinitionVersion, """["Article",1]""", """["Article__imported",1]"""),
                (remapped.EntityKind, remapped.SourceId, remapped.TargetId));
            Assert.Equal("Article__imported", (await target.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("article-1"))!.DefinitionId);
            Assert.Equal("Author", (await target.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("author-1"))!.DefinitionId);
        }
    }

    /// <summary>One environment: its own service provider over its own store.</summary>
    private sealed class Environment(ServiceProvider services, IAsterTestDatabase? database) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public static Environment Create(string kind)
        {
            IAsterTestDatabase? database = kind switch
            {
                InMemory => null,
                Sqlite => new SqliteTestDatabase(),
                Postgres => new PostgresTestDatabase(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

            try
            {
                return new Environment(database?.CreateProvider() ?? new ServiceCollection().AddAsterCore().BuildServiceProvider(), database);
            }
            catch
            {
                database?.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            database?.Dispose();
        }
    }
}
