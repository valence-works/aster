using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #93: resource definitions attach shared aspects by pin. <c>BuildAsync</c> resolves the latest or the
/// pinned version from <see cref="IAspectDefinitionStore"/>, embeds it unchanged with
/// <see cref="AspectDefinition.IsShared"/>, and rejects missing aspects with stable diagnostics.
/// Definition-local aspects behave as before.
/// </summary>
public sealed class SharedAspectPinningTests : IDisposable
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

    private static FacetDefinition Facet(string id, string type, bool isRequired = false) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        IsRequired = isRequired,
    };

    private static AspectDefinition Aspect(string id, params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 0,
        FacetDefinitions = facets,
    };

    private static string Json(AspectDefinition aspect) => JsonSerializer.Serialize(aspect);

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task TwoDefinitionsAttachingTheSameVersion_EmbedIdenticalSnapshotsAndExposeThePin(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var seo = await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string", isRequired: true)));

        await definitions.RegisterDefinitionAsync(await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects));
        await definitions.RegisterDefinitionAsync(await new ResourceDefinitionBuilder().WithDefinitionId("Page").WithSharedAspect("Seo", version: 1).BuildAsync(aspects));

        var article = (await definitions.GetDefinitionAsync("Article"))!.AspectDefinitions["Seo"];
        var page = (await definitions.GetDefinitionAsync("Page"))!.AspectDefinitions["Seo"];
        Assert.True(article.IsShared);
        Assert.Equal(("Seo", 1, seo.Id), (article.AspectDefinitionId, article.Version, article.Id));
        Assert.Equal(Json(article), Json(page));
        Assert.Equal(Json(seo with { IsShared = true }), Json(article));
    }

    [Fact]
    public async Task NullVersion_PinsTheLatest_AndAnExplicitVersionPinsThatVersion()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")));
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string"), Facet("CanonicalUrl", "string")));

        var latest = await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects);
        var pinned = await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo", 1).BuildAsync(aspects);

        Assert.Equal(2, latest.AspectDefinitions["Seo"].Version);
        Assert.Equal(2, latest.AspectDefinitions["Seo"].FacetDefinitions.Count);
        Assert.Equal(1, pinned.AspectDefinitions["Seo"].Version);
        Assert.Single(pinned.AspectDefinitions["Seo"].FacetDefinitions);
    }

    [Fact]
    public async Task MissingAspectOrVersion_IsRejectedWithEveryDiagnostic()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")));

        var exception = await Assert.ThrowsAsync<SharedAspectResolutionException>(async () =>
            await new ResourceDefinitionBuilder()
                .WithDefinitionId("Article")
                .WithSharedAspect("Seo", 3)
                .WithSharedAspect("Owner")
                .WithAspect("Local").WithFacet("Note", "string")
                .BuildAsync(aspects));

        Assert.Equal(
            new[]
            {
                (SharedAspectDiagnosticCodes.AspectDefinitionNotFound, "Seo", (int?)3),
                (SharedAspectDiagnosticCodes.AspectDefinitionNotFound, "Owner", (int?)null),
            },
            exception.Diagnostics.Select(static d => (d.Code, d.AttachmentKey, d.Version)));
    }

    [Fact]
    public async Task AspectsAreResolvedInTheBuildTenantOnly()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var tenantA = TenantScope.FromTenantId("tenant-a");
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")), tenantA);

        var resolved = await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects, tenantA);
        var exception = await Assert.ThrowsAsync<SharedAspectResolutionException>(async () =>
            await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects, TenantScope.FromTenantId("tenant-b")));

        Assert.True(resolved.AspectDefinitions["Seo"].IsShared);
        Assert.Equal(SharedAspectDiagnosticCodes.AspectDefinitionNotFound, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public async Task NamedAttachments_PinLikeUnnamedOnes_AndRequiresNameIsEnforced()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Audit", Facet("At", "datetime")) with { RequiresName = true });

        var definition = await new ResourceDefinitionBuilder()
            .WithDefinitionId("Order")
            .WithSharedAspect("Audit", name: "Created")
            .WithSharedAspect("Audit", name: "Shipped")
            .BuildAsync(aspects);
        var exception = await Assert.ThrowsAsync<SharedAspectResolutionException>(async () =>
            await new ResourceDefinitionBuilder().WithDefinitionId("Order").WithSharedAspect("Audit").BuildAsync(aspects));

        Assert.Equal(new[] { "Audit:Created", "Audit:Shipped" }, definition.AspectDefinitions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Json(definition.AspectDefinitions["Audit:Created"]), Json(definition.AspectDefinitions["Audit:Shipped"]));
        Assert.True(definition.AspectDefinitions["Audit:Created"].RequiresName);
        Assert.Equal(SharedAspectDiagnosticCodes.AspectNameRequired, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public void Build_WithSharedAspects_Throws_AndFacetsCannotBeAddedToThem()
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.WithFacet("Extra", "string"));
        Assert.Throws<InvalidOperationException>(() => builder.WithFacetMetadata(new FacetMetadata { Label = "x" }));
        Assert.Throws<InvalidOperationException>(() => builder.WithFacetMetadata("Slug", new FacetMetadata { Label = "x" }));
    }

    [Fact]
    public async Task DuplicateSharedAttachment_IsRejected()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")));

        await Assert.ThrowsAsync<DuplicateAspectAttachmentException>(async () =>
            await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").WithAspect("Seo").BuildAsync(aspects));
    }

    [Fact]
    public async Task EmbeddedOnlyDefinitions_AreUnchanged()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Article").WithAspect("Seo").WithFacet("Slug", "string");

        var built = builder.Build();
        var builtAsync = await builder.BuildAsync(aspects);

        Assert.False(built.AspectDefinitions["Seo"].IsShared);
        Assert.Equal(1, built.AspectDefinitions["Seo"].Version);
        Assert.Equal(Json(built.AspectDefinitions["Seo"]), Json(builtAsync.AspectDefinitions["Seo"]));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task SaveValidation_UsesTheEmbeddedSharedSnapshot(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string", isRequired: true)));
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(
            await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects));

        // A later aspect version changes nothing for the existing definition.
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")));
        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await provider.GetRequiredService<IResourceManager>().CreateAsync("Article", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object> { ["Seo"] = new Dictionary<string, object>() },
            }));

        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public async Task FromType_RegistersATypedAspect_ThatWithSharedAspectOfTAttaches()
    {
        await using var provider = CreateProvider(InMemory);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(AspectDefinitionBuilder.FromType<SeoAspect>());

        var definition = await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect<SeoAspect>().BuildAsync(aspects);

        var seo = definition.AspectDefinitions[nameof(SeoAspect)];
        Assert.True(seo.IsShared);
        Assert.Equal(1, seo.Version);
        Assert.Equal(
            new[] { ("Slug", "string"), ("Priority", "int") },
            seo.FacetDefinitions.Select(static facet => (facet.FacetDefinitionId, facet.Type)));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RegisteringAnEmbeddedCopy_StoresItAsNotShared(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Aspect("Seo", Facet("Slug", "string")));
        var embedded = (await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo").BuildAsync(aspects)).AspectDefinitions["Seo"];

        var registered = await aspects.RegisterAsync(embedded);

        Assert.False(registered.IsShared);
        Assert.False((await aspects.GetLatestAsync("Seo"))!.IsShared);
    }

    private sealed class SeoAspect
    {
        public string Slug { get; init; } = string.Empty;

        public int Priority { get; init; }
    }
}
