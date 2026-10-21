using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #94: previewing and applying shared aspect upgrades. Preview lists older pins with a facet diff and
/// writes nothing; apply registers new definition versions for the selected definitions only; resources
/// then move through the existing schema upgrade flow.
/// </summary>
public sealed class AspectUpgradeTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static FacetDefinition Facet(string id, string type, bool isRequired = false, FacetMetadata? metadata = null) => new()
    {
        FacetDefinitionId = id,
        Id = $"facet-{id}",
        Version = 1,
        Type = type,
        IsRequired = isRequired,
        Metadata = metadata,
    };

    private static AspectDefinition Aspect(string id, params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 0,
        FacetDefinitions = facets,
    };

    /// <summary>
    /// Seo v1 (Slug); Article and Page pin v1; Blog has a definition-local Seo; Seo v2 adds CanonicalUrl and caps Slug at 5.
    /// </summary>
    private static async Task<(IAspectDefinitionStore Aspects, IResourceDefinitionStore Definitions)> SeedAsync(ServiceProvider provider)
    {
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string", isRequired: true)));
        foreach (var definitionId in new[] { "Page", "Article" })
            await definitions.RegisterDefinitionAsync(await new ResourceDefinitionBuilder().WithDefinitionId(definitionId).WithSharedAspect("Seo").BuildAsync(aspects));
        await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder().WithDefinitionId("Blog").WithAspect("Seo").WithFacet("Slug", "string").Build());
        await aspects.RegisterAsync(Aspect(
            "Seo",
            Facet("Slug", "string", isRequired: true, new FacetMetadata { MaxLength = 5 }),
            Facet("CanonicalUrl", "string")));

        return (aspects, definitions);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Preview_ListsOlderPinsWithAFacetDiff_AndWritesNothing(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var (_, definitions) = await SeedAsync(provider);

        var preview = await provider.GetRequiredService<IAspectUpgradeService>().PreviewAsync("Seo");

        Assert.Equal(2, preview.TargetVersion);
        Assert.Empty(preview.Diagnostics);
        Assert.Equal(
            new[] { ("Article", 1, "Seo", 1), ("Page", 1, "Seo", 1) },
            preview.Candidates.Select(static c => (c.DefinitionId, c.DefinitionVersion, c.AttachmentKey, c.PinnedVersion)));
        Assert.Equal(
            new[] { ("CanonicalUrl", AspectFacetChangeKind.Added), ("Slug", AspectFacetChangeKind.MetadataChanged) },
            preview.Candidates[0].Changes.Select(static change => (change.FacetDefinitionId, change.Kind)));
        Assert.Null(preview.Candidates[0].Changes[1].From);
        Assert.Contains("\"MaxLength\":5", preview.Candidates[0].Changes[1].To);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Article"))!.Version);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Page"))!.Version);
    }

    [Fact]
    public async Task Preview_ToAnOlderOrMissingTarget()
    {
        await using var provider = CreateProvider(InMemory);
        await SeedAsync(provider);
        var upgrades = provider.GetRequiredService<IAspectUpgradeService>();

        var toV1 = await upgrades.PreviewAsync("Seo", 1);
        var missing = await upgrades.PreviewAsync("Seo", 9);

        Assert.Empty(toV1.Candidates);
        Assert.Null(missing.TargetVersion);
        Assert.Equal(SharedAspectDiagnosticCodes.AspectDefinitionNotFound, Assert.Single(missing.Diagnostics).Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Apply_RePinsOnlyTheSelectedDefinitions(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var (_, definitions) = await SeedAsync(provider);

        var result = await provider.GetRequiredService<IAspectUpgradeService>().ApplyAsync(new AspectUpgradeRequest
        {
            AspectDefinitionId = "Seo",
            DefinitionIds = ["Article", "Blog", "Article", "Missing"],
        });

        Assert.Equal(2, result.TargetVersion);
        Assert.Equal(
            new[]
            {
                ("Article", AspectUpgradeStatus.Upgraded, (int?)2),
                ("Blog", AspectUpgradeStatus.NotApplicable, (int?)null),
                ("Missing", AspectUpgradeStatus.NotApplicable, (int?)null),
            },
            result.Outcomes.Select(static o => (o.DefinitionId, o.Status, o.NewDefinitionVersion)));
        Assert.Equal(new[] { "Seo" }, result.Outcomes[0].AttachmentKeys);

        var article = (await definitions.GetDefinitionAsync("Article"))!;
        Assert.Equal(2, article.Version);
        Assert.True(article.AspectDefinitions["Seo"].IsShared);
        Assert.Equal(2, article.AspectDefinitions["Seo"].Version);
        Assert.Equal(1, (await definitions.GetDefinitionVersionAsync("Article", 1))!.AspectDefinitions["Seo"].Version);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Page"))!.Version);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Blog"))!.Version);

        var again = await provider.GetRequiredService<IAspectUpgradeService>().ApplyAsync(new AspectUpgradeRequest
        {
            AspectDefinitionId = "Seo",
            DefinitionIds = ["Article"],
        });
        Assert.Equal(AspectUpgradeStatus.NotApplicable, Assert.Single(again.Outcomes).Status);
    }

    [Fact]
    public async Task Apply_RePinsEveryNamedAttachment()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Audit", Facet("At", "datetime")));
        await definitions.RegisterDefinitionAsync(await new ResourceDefinitionBuilder()
            .WithDefinitionId("Order")
            .WithSharedAspect("Audit", name: "Created")
            .WithSharedAspect("Audit", name: "Shipped")
            .BuildAsync(aspects));
        await aspects.RegisterAsync(Aspect("Audit", Facet("At", "datetime"), Facet("By", "string")));

        var result = await provider.GetRequiredService<IAspectUpgradeService>().ApplyAsync(new AspectUpgradeRequest
        {
            AspectDefinitionId = "Audit",
            DefinitionIds = ["Order"],
        });

        Assert.Equal(new[] { "Audit:Created", "Audit:Shipped" }, Assert.Single(result.Outcomes).AttachmentKeys);
        var order = (await definitions.GetDefinitionAsync("Order"))!;
        Assert.All(order.AspectDefinitions.Values, static aspect => Assert.Equal(2, aspect.Version));
    }

    [Fact]
    public async Task Apply_TargetRequiringANameForAnUnnamedAttachment_FailsAndWritesNothing()
    {
        await using var provider = CreateProvider(InMemory);
        var (aspects, definitions) = await SeedAsync(provider);
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")) with { RequiresName = true });

        var result = await provider.GetRequiredService<IAspectUpgradeService>().ApplyAsync(new AspectUpgradeRequest
        {
            AspectDefinitionId = "Seo",
            DefinitionIds = ["Article"],
        });

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(AspectUpgradeStatus.Failed, outcome.Status);
        Assert.Equal(SharedAspectDiagnosticCodes.AspectNameRequired, Assert.Single(outcome.Diagnostics).Code);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Article"))!.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task EndToEnd_AspectV2_ThenRePin_ThenResourceSchemaUpgrade(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string", isRequired: true)));
        await definitions.RegisterDefinitionAsync(await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects));
        var article = await manager.CreateAsync("Article", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Seo"] = new Dictionary<string, object> { ["Slug"] = "hello" } },
        });

        // Aspect v2 caps Slug at 5 characters. Registering it and re-pinning change nothing for the resource.
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string", isRequired: true, new FacetMetadata { MaxLength = 5 })));
        await provider.GetRequiredService<IAspectUpgradeService>().ApplyAsync(new AspectUpgradeRequest
        {
            AspectDefinitionId = "Seo",
            DefinitionIds = ["Article"],
        });
        var longSlug = await manager.UpdateAsync(article.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = new Dictionary<string, object> { ["Seo"] = new Dictionary<string, object> { ["Slug"] = "hello-world" } },
        });
        Assert.Equal(1, longSlug.DefinitionVersion);

        // The explicit schema upgrade moves the resource onto definition v2, whose rules then apply.
        var upgraded = await provider.GetRequiredService<IResourceSchemaVersionService>().UpgradeAsync(article.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = longSlug.Version,
            TargetDefinitionVersion = 2,
            AspectUpdates = new Dictionary<string, object> { ["Seo"] = new Dictionary<string, object> { ["Slug"] = "short" } },
        });
        Assert.Equal(2, upgraded.Resource!.DefinitionVersion);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.UpdateAsync(article.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = upgraded.Resource.Version,
                AspectUpdates = new Dictionary<string, object> { ["Seo"] = new Dictionary<string, object> { ["Slug"] = "too-long" } },
            }));
        Assert.Equal(FacetValueDiagnosticCodes.LengthOutOfRange, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public void Diff_ReportsTypeAndRequiredChanges()
    {
        var changes = Aster.Core.Services.AspectUpgradeService.Diff(
            Aspect("X", Facet("A", "string"), Facet("B", "int", isRequired: true), Facet("Gone", "bool")),
            Aspect("X", Facet("A", "string[]", isRequired: true), Facet("B", "int", isRequired: true)));

        Assert.Equal(
            new[]
            {
                ("A", AspectFacetChangeKind.TypeChanged, (string?)"string", (string?)"string[]"),
                ("A", AspectFacetChangeKind.RequiredChanged, "false", "true"),
                ("Gone", AspectFacetChangeKind.Removed, "bool", (string?)null),
            },
            changes.Select(static c => (c.FacetDefinitionId, c.Kind, c.From, c.To)));
    }
}
