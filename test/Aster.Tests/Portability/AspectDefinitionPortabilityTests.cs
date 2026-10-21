using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

/// <summary>
/// #95: portable snapshots carry the shared aspect definition versions their definitions pin; import registers
/// them with versions preserved before the definitions, reports unresolved and mismatched pins with stable
/// codes, never remaps a divergent aspect version, and leaves snapshots without aspect definitions unchanged.
/// #195: aspect versions compare by content, not by their generated IDs, so a tenant that registered the same
/// aspect independently reuses its own copy.
/// </summary>
public sealed class AspectDefinitionPortabilityTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string PinPath = "definitions/Article/1/aspectDefinitions/Seo";

    private static readonly TenantScope Source = TenantScope.FromTenantId("source");
    private static readonly TenantScope Target = TenantScope.FromTenantId("target");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static AspectDefinition Seo(params string[] facetIds) => new()
    {
        AspectDefinitionId = "Seo",
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        FacetDefinitions = (facetIds.Length > 0 ? facetIds : ["Slug"])
            .Select(static id => new FacetDefinition { FacetDefinitionId = id, Id = $"{id}-id", Version = 1, Type = "string" })
            .ToList(),
    };

    /// <summary>Registers Seo v1 (Slug) and v2 (Slug, CanonicalUrl) and an unpinned Audit aspect in
    /// <see cref="Source"/>, then an Article definition pinning Seo at <paramref name="pinnedVersion"/>.</summary>
    private static async Task<IReadOnlyList<AspectDefinition>> SeedSourceAsync(IServiceProvider provider, int pinnedVersion)
    {
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var registered = new List<AspectDefinition>
        {
            await aspects.RegisterAsync(Seo(), Source),
            await aspects.RegisterAsync(Seo("Slug", "CanonicalUrl"), Source),
        };
        await aspects.RegisterAsync(Seo() with { AspectDefinitionId = "Audit" }, Source);

        var definition = await new ResourceDefinitionBuilder()
            .WithDefinitionId("Article")
            .WithSharedAspect("Seo", pinnedVersion)
            .BuildAsync(aspects, Source);
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition, Source, CancellationToken.None);

        return registered;
    }

    private static async Task<PortableSnapshot> ExportArticleAsync(
        IServiceProvider provider,
        PortableExportScopeMode scopeMode = PortableExportScopeMode.DefinitionsOnly)
    {
        var result = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            TenantScope = Source,
            ScopeMode = scopeMode,
            DefinitionIds = ["Article"],
        });

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == PortableDiagnosticSeverity.Error);
        return result.Snapshot!;
    }

    [Theory]
    [InlineData(InMemory, PortableExportScopeMode.DefinitionsOnly)]
    [InlineData(InMemory, PortableExportScopeMode.DefinitionWithResources)]
    [InlineData(Sqlite, PortableExportScopeMode.DefinitionsOnly)]
    [InlineData(Sqlite, PortableExportScopeMode.DefinitionWithResources)]
    public async Task Export_IncludesOnlyThePinnedAspectVersions(string providerKind, PortableExportScopeMode scopeMode)
    {
        await using var provider = CreateProvider(providerKind);
        var registered = await SeedSourceAsync(provider, pinnedVersion: 1);

        var snapshot = await ExportArticleAsync(provider, scopeMode);

        var aspect = Assert.Single(snapshot.AspectDefinitions);
        Assert.Equal(("Seo", 1, registered[0].Id), (aspect.AspectDefinitionId, aspect.Version, aspect.Id));
        Assert.False(aspect.IsShared);
        Assert.Equal(1, (await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            TenantScope = Source,
            ScopeMode = scopeMode,
            DefinitionIds = ["Article"],
        })).ToSummary().AspectDefinitionCount);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Import_PreservesAspectVersionsAndPins(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var registered = await SeedSourceAsync(provider, pinnedVersion: 2);
        var snapshot = await ExportArticleAsync(provider);

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ImportAsync(
            snapshot,
            new PortableImportOptions { TargetTenantScope = Target });

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Equal(1, result.Counts.AspectDefinitions);
        Assert.Contains(result.IdentityMap, static mapping =>
            mapping.EntityKind == PortableEntityKind.AspectDefinitionVersion
            && mapping.Reason == PortableIdentityMappingReason.Preserved);

        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var imported = Assert.Single(await aspects.ListVersionsAsync("Seo", Target));
        Assert.Equal((2, registered[1].Id), (imported.Version, imported.Id));
        Assert.Equal(new[] { "Slug", "CanonicalUrl" }, imported.FacetDefinitions.Select(static f => f.FacetDefinitionId));
        Assert.Null(await aspects.GetLatestAsync("Audit", Target));

        var definition = await provider.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("Article", Target, CancellationToken.None);
        var pin = definition!.AspectDefinitions["Seo"];
        Assert.Equal(("Seo", 2, true), (pin.AspectDefinitionId, pin.Version, pin.IsShared));

        // The target registry continues from the imported version, and the pin resolves there.
        Assert.Equal(3, (await aspects.RegisterAsync(Seo(), Target)).Version);
        var rebuilt = await new ResourceDefinitionBuilder().WithDefinitionId("Page").WithSharedAspect("Seo", 2).BuildAsync(aspects, Target);
        Assert.Equal(registered[1].Id, rebuilt.AspectDefinitions["Seo"].Id);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ImportingTheSameSnapshotAgain_ReusesTheAspectVersion(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedSourceAsync(provider, pinnedVersion: 1);
        var snapshot = await ExportArticleAsync(provider);
        var portability = provider.GetRequiredService<IResourcePortabilityService>();
        var options = new PortableImportOptions { TargetTenantScope = Target };

        await portability.ImportAsync(snapshot, options);
        var again = await portability.ImportAsync(snapshot, options);

        Assert.Equal(PortableImportStatus.NoOp, again.Status);
        Assert.Equal(0, again.Counts.AspectDefinitions);
        Assert.Contains(again.IdentityMap, static mapping =>
            mapping.EntityKind == PortableEntityKind.AspectDefinitionVersion
            && mapping.Reason == PortableIdentityMappingReason.ReusedIdentical);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task SnapshotWithoutAspectDefinitions_ImportsAsBefore(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definition = new ResourceDefinitionBuilder().WithDefinitionId("Product").WithAspect("Price").WithFacet("Amount", "decimal").Build();
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition, Source, CancellationToken.None);
        var exported = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            TenantScope = Source,
            ScopeMode = PortableExportScopeMode.DefinitionsOnly,
            DefinitionIds = ["Product"],
        });

        // A snapshot serialized before #95 has no aspectDefinitions property at all.
        var json = JsonSerializer.SerializeToNode(exported.Snapshot, JsonOptions)!.AsObject();
        Assert.True(json.Remove("aspectDefinitions"));
        var legacy = json.Deserialize<PortableSnapshot>(JsonOptions)!;

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ImportAsync(
            legacy,
            new PortableImportOptions { TargetTenantScope = Target });

        Assert.Empty(legacy.AspectDefinitions);
        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Equal((0, 1), (result.Counts.AspectDefinitions, result.Counts.Definitions));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UnresolvedPin_FailsWithAStableCodeAndPath_UnlessTheTargetHasTheVersion(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var registered = await SeedSourceAsync(provider, pinnedVersion: 1);
        var snapshot = await ExportArticleAsync(provider) with { AspectDefinitions = [] };
        var portability = provider.GetRequiredService<IResourcePortabilityService>();
        var options = new PortableImportOptions { TargetTenantScope = Target };

        var preview = await portability.PreviewImportAsync(snapshot, options);

        Assert.False(preview.CanImport);
        var diagnostic = Assert.Single(preview.Diagnostics);
        Assert.Equal((PortableDiagnosticCodes.UnresolvedAspectPin, PortableDiagnosticSeverity.Error, PinPath), (diagnostic.Code, diagnostic.Severity, diagnostic.Path));

        // The same version, already registered in the target, resolves the pin.
        await provider.GetRequiredService<IAspectDefinitionStore>().RegisterAsync(registered[0], Target);
        preview = await portability.PreviewImportAsync(snapshot, options);

        Assert.True(preview.CanImport);
        Assert.Equal((0, 1), (preview.Counts.AspectDefinitions, preview.Counts.Definitions));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task PinThatDiffersFromTheTargetVersion_FailsWithAspectPinMismatch(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedSourceAsync(provider, pinnedVersion: 1);
        var snapshot = await ExportArticleAsync(provider) with { AspectDefinitions = [] };
        await provider.GetRequiredService<IAspectDefinitionStore>().RegisterAsync(Seo("Title"), Target);

        var preview = await provider.GetRequiredService<IResourcePortabilityService>().PreviewImportAsync(
            snapshot,
            new PortableImportOptions { TargetTenantScope = Target });

        Assert.False(preview.CanImport);
        var diagnostic = Assert.Single(preview.Diagnostics);
        Assert.Equal((PortableDiagnosticCodes.AspectPinMismatch, PinPath), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData(InMemory, false)]
    [InlineData(InMemory, true)]
    [InlineData(Sqlite, false)]
    [InlineData(Sqlite, true)]
    public async Task AspectRegisteredIndependentlyInTheTarget_IsReused_AndImportedPinsUseTheTargetCopy(string providerKind, bool targetHasArticle)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedSourceAsync(provider, pinnedVersion: 1);
        var snapshot = await ExportArticleAsync(provider);

        // The same aspect content with its own generated aspect and facet IDs, as a separate registration produces.
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        var own = await aspects.RegisterAsync(Seo() with
        {
            FacetDefinitions = [new FacetDefinition { FacetDefinitionId = "Slug", Id = Guid.NewGuid().ToString(), Version = 1, Type = "string" }],
        }, Target);
        Assert.NotEqual(snapshot.AspectDefinitions[0].Id, own.Id);
        if (targetHasArticle)
        {
            // Its own Article too, so the snapshot's Article collides and is remapped.
            await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(
                await new ResourceDefinitionBuilder().WithDefinitionId("Article").WithSharedAspect("Seo", 1).BuildAsync(aspects, Target),
                Target,
                CancellationToken.None);
        }

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ImportAsync(
            snapshot,
            new PortableImportOptions { TargetTenantScope = Target, CollisionMode = PortableImportCollisionMode.RemapDivergent });

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Contains(result.IdentityMap, static mapping =>
            mapping.EntityKind == PortableEntityKind.AspectDefinitionVersion
            && mapping.Reason == PortableIdentityMappingReason.ReusedIdentical);
        Assert.Equal(own.Id, Assert.Single(await aspects.ListVersionsAsync("Seo", Target)).Id);

        var importedId = targetHasArticle
            ? Assert.Single(result.IdentityMap, static mapping =>
                mapping.EntityKind == PortableEntityKind.DefinitionVersion && mapping.Reason == PortableIdentityMappingReason.RemappedDivergent).TargetId
            : null;
        var definitionId = importedId is null ? "Article" : JsonSerializer.Deserialize<JsonArray>(importedId)![0]!.GetValue<string>();
        var imported = await provider.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync(definitionId, Target, CancellationToken.None);
        var pin = imported!.AspectDefinitions["Seo"];
        Assert.Equal((own.Id, own.FacetDefinitions[0].Id, true), (pin.Id, pin.FacetDefinitions[0].Id, pin.IsShared));
    }

    [Theory]
    [InlineData(InMemory, PortableImportCollisionMode.Strict)]
    [InlineData(InMemory, PortableImportCollisionMode.RemapDivergent)]
    [InlineData(Sqlite, PortableImportCollisionMode.Strict)]
    [InlineData(Sqlite, PortableImportCollisionMode.RemapDivergent)]
    public async Task DivergentAspectVersion_IsNeverRemapped(string providerKind, PortableImportCollisionMode collisionMode)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedSourceAsync(provider, pinnedVersion: 1);
        var snapshot = await ExportArticleAsync(provider);
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();
        await aspects.RegisterAsync(Seo("Title"), Target);

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ImportAsync(
            snapshot,
            new PortableImportOptions { TargetTenantScope = Target, CollisionMode = collisionMode });

        Assert.Equal(PortableImportStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, static d =>
            d.Code == PortableDiagnosticCodes.DivergentIdentityCollision
            && d.Severity == PortableDiagnosticSeverity.Error
            && d.Path == "aspectDefinitions/Seo/1");
        Assert.Null(await provider.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("Article", Target, CancellationToken.None));
        Assert.Equal(new[] { "Title" }, (await aspects.GetLatestAsync("Seo", Target))!.FacetDefinitions.Select(static f => f.FacetDefinitionId));
    }

    [Fact]
    public async Task Validate_ReportsAPinThatDiffersFromTheSnapshotAspect()
    {
        await using var provider = CreateProvider(InMemory);
        await SeedSourceAsync(provider, pinnedVersion: 1);
        var snapshot = await ExportArticleAsync(provider);
        var tampered = snapshot with { AspectDefinitions = [snapshot.AspectDefinitions[0] with { RequiresName = true }] };

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ValidateAsync(tampered);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((PortableDiagnosticCodes.AspectPinMismatch, PinPath), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public async Task Validate_ReportsMalformedAndDuplicateAspectEntries()
    {
        await using var provider = CreateProvider(InMemory);
        var seo = Seo();
        var snapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            AspectDefinitions = [seo, seo, seo with { Version = 0 }, null!],
        };

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ValidateAsync(snapshot);

        Assert.Equal(
            new[]
            {
                (PortableDiagnosticCodes.MalformedSnapshot, "aspectDefinitions/2/version"),
                (PortableDiagnosticCodes.MalformedSnapshot, "aspectDefinitions/3"),
                (PortableDiagnosticCodes.DuplicateSnapshotIdentity, """aspectDefinitions/["Seo",1]"""),
            },
            result.Diagnostics.Select(static d => (d.Code, d.Path!)));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Export_WarnsWhenAPinnedVersionIsMissingFromTheSourceStore(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definition = new ResourceDefinition
        {
            DefinitionId = "Article",
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            AspectDefinitions = new Dictionary<string, AspectDefinition> { ["Seo"] = Seo() with { IsShared = true } },
        };
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition, Source, CancellationToken.None);

        var result = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            TenantScope = Source,
            ScopeMode = PortableExportScopeMode.DefinitionsOnly,
            DefinitionIds = ["Article"],
        });

        Assert.Empty(result.Snapshot!.AspectDefinitions);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (PortableDiagnosticCodes.UnresolvedAspectPin, PortableDiagnosticSeverity.Warning, PinPath),
            (diagnostic.Code, diagnostic.Severity, diagnostic.Path));
    }
}
