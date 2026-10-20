using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// #86: the <c>reference</c> and <c>reference[]</c> facet types (ADR 0002 §6) register with
/// TargetDefinitionIds and ReferenceResolution metadata, validate the value shape on save, persist
/// and export their metadata, and resolve through <see cref="IResourceReferenceResolver"/> with
/// latest and channel semantics, identically for both providers.
/// </summary>
public sealed class ReferenceFacetTypeTests : IDisposable
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

    private static FacetDefinition Facet(string id, string type, FacetMetadata? metadata = null) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        Metadata = metadata,
    };

    private static ResourceDefinition Definition(string definitionId, params FacetDefinition[] facets) => new()
    {
        DefinitionId = definitionId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            ["Links"] = new()
            {
                AspectDefinitionId = "Links",
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static ResourceDefinition AgentDefinition(string resolution = "latest") => Definition(
        "Agent",
        Facet("Secret", "reference", new FacetMetadata { TargetDefinitionIds = ["Secret"], ReferenceResolution = resolution }),
        Facet("Tools", "reference[]", new FacetMetadata { TargetDefinitionIds = ["Tool", "Workflow"], UniqueItems = true }));

    private static async Task<IResourceManager> SeedTargetsAsync(ServiceProvider provider, TenantScope? tenant = null)
    {
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        foreach (var definitionId in new[] { "Secret", "Tool" })
        {
            var definition = Definition(definitionId, Facet("Name", "string"));
            if (tenant is null)
                await definitions.RegisterDefinitionAsync(definition);
            else
                await definitions.RegisterDefinitionAsync(definition, tenant, CancellationToken.None);
        }

        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.CreateAsync("Secret", new CreateResourceRequest { ResourceId = "sec-openai", TenantScope = tenant });
        await manager.CreateAsync("Tool", new CreateResourceRequest { ResourceId = "tool-search", TenantScope = tenant });
        return manager;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Registration
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RegisterDefinitionAsync_ReferenceFacets_PersistTheirMetadata(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();

        await definitions.RegisterDefinitionAsync(AgentDefinition(resolution: "production"));

        var stored = await definitions.GetDefinitionAsync("Agent");
        var facets = stored!.AspectDefinitions["Links"].FacetDefinitions;
        Assert.Equal(["reference", "reference[]"], facets.Select(static facet => facet.Type));
        Assert.Equal(["Secret"], facets[0].Metadata!.TargetDefinitionIds!);
        Assert.Equal("production", facets[0].Metadata!.ReferenceResolution);
        Assert.Equal(["Tool", "Workflow"], facets[1].Metadata!.TargetDefinitionIds!);
    }

    [Theory]
    [InlineData("Reference")]
    [InlineData("REFERENCE[]")]
    public async Task RegisterDefinitionAsync_ReferenceTypeIsCaseInsensitive(string type)
    {
        await using var provider = CreateProvider(InMemory);

        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(Definition("Agent", Facet("Target", type)));
    }

    public static TheoryData<string, FacetMetadata, string> InvalidMetadata => new()
    {
        { "string", new FacetMetadata { TargetDefinitionIds = ["Secret"] }, FacetMetadataDiagnosticCodes.ConstraintInapplicable },
        { "guid[]", new FacetMetadata { ReferenceResolution = "latest" }, FacetMetadataDiagnosticCodes.ConstraintInapplicable },
        { "reference", new FacetMetadata { TargetDefinitionIds = [] }, FacetMetadataDiagnosticCodes.ReferenceTargetsInvalid },
        { "reference", new FacetMetadata { TargetDefinitionIds = ["Secret", " "] }, FacetMetadataDiagnosticCodes.ReferenceTargetsInvalid },
        { "reference[]", new FacetMetadata { TargetDefinitionIds = ["Secret", "Secret"] }, FacetMetadataDiagnosticCodes.ReferenceTargetsInvalid },
        { "reference", new FacetMetadata { ReferenceResolution = "  " }, FacetMetadataDiagnosticCodes.ReferenceResolutionInvalid },
        { "reference", new FacetMetadata { Pattern = "^sec-" }, FacetMetadataDiagnosticCodes.ConstraintInapplicable },
    };

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public async Task RegisterDefinitionAsync_InvalidReferenceMetadata_IsRejected(string type, FacetMetadata metadata, string code)
    {
        await using var provider = CreateProvider(InMemory);

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(Definition("Agent", Facet("Target", type, metadata))));

        Assert.Equal(code, Assert.Single(exception.Diagnostics).Code);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save
    // ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, object> MismatchedValues => new()
    {
        { InMemory, "Secret", 42 },
        { Sqlite, "Secret", "" },
        { InMemory, "Secret", "   " },
        { Sqlite, "Secret", new[] { "sec-openai" } },
        { InMemory, "Tools", "tool-search" },
        { Sqlite, "Tools", new object[] { "tool-search", 7 } },
    };

    [Theory]
    [MemberData(nameof(MismatchedValues))]
    public async Task CreateAsync_NonResourceIdValue_IsTypeMismatch(string providerKind, string facet, object value)
    {
        await using var provider = CreateProvider(providerKind);
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(AgentDefinition());

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await provider.GetRequiredService<IResourceManager>().CreateAsync("Agent", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object> { ["Links"] = new Dictionary<string, object> { [facet] = value } },
            }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal(facet, diagnostic.FacetDefinitionId);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task CreateAsync_ReferenceValues_SaveAndQueryAsStrings(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await SeedTargetsAsync(provider);
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(AgentDefinition());

        var agent = await manager.CreateAsync("Agent", new CreateResourceRequest
        {
            ResourceId = "agent-summarizer",
            InitialAspects = new Dictionary<string, object>
            {
                ["Links"] = new Dictionary<string, object> { ["Secret"] = "sec-openai", ["Tools"] = new[] { "tool-search" } },
            },
        });

        var queries = provider.GetRequiredService<IResourceQueryService>();
        var bySecret = await queries.QueryAsync(new ResourceQuery { DefinitionId = "Agent", Filter = new FacetValueFilter("Links", "Secret", "sec-openai", ComparisonOperator.Equals) });
        var byTool = await queries.QueryAsync(new ResourceQuery { DefinitionId = "Agent", Filter = new FacetValueFilter("Links", "Tools", "tool-search", ComparisonOperator.ArrayContains) });
        Assert.Equal(agent.ResourceId, Assert.Single(bySecret).ResourceId);
        Assert.Equal(agent.ResourceId, Assert.Single(byTool).ResourceId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Resolution
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ResolveAsync_Latest_ReturnsTheLatestTargetVersion(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await SeedTargetsAsync(provider);
        await manager.UpdateAsync("sec-openai", new UpdateResourceRequest { BaseVersion = 1 });
        var resolver = provider.GetRequiredService<IResourceReferenceResolver>();

        var resolution = await resolver.ResolveAsync(AgentDefinition(), "Links", "Secret", "sec-openai");

        Assert.Equal(ResourceReferenceResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(2, resolution.Resource!.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ResolveAsync_Channel_ReturnsTheActiveVersionOrNotActive(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await SeedTargetsAsync(provider);
        await manager.UpdateAsync("sec-openai", new UpdateResourceRequest { BaseVersion = 1 });
        var resolver = provider.GetRequiredService<IResourceReferenceResolver>();
        var definition = AgentDefinition(resolution: "production");

        var beforeActivation = await resolver.ResolveAsync(definition, "Links", "Secret", "sec-openai");
        await manager.ActivateAsync("sec-openai", 1, "production");
        var afterActivation = await resolver.ResolveAsync(definition, "Links", "Secret", "sec-openai");

        Assert.Equal(ResourceReferenceResolutionStatus.NotActiveInChannel, beforeActivation.Status);
        Assert.Null(beforeActivation.Resource);
        Assert.True(afterActivation.IsResolved);
        Assert.Equal(1, afterActivation.Resource!.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ResolveAsync_MultipleActiveVersions_ReturnsTheNewest(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await SeedTargetsAsync(provider);
        await manager.UpdateAsync("sec-openai", new UpdateResourceRequest { BaseVersion = 1 });
        await manager.ActivateAsync("sec-openai", 1, "production", allowMultipleActive: true);
        await manager.ActivateAsync("sec-openai", 2, "production", allowMultipleActive: true);

        var resolution = await provider.GetRequiredService<IResourceReferenceResolver>()
            .ResolveAsync(AgentDefinition(resolution: "production"), "Links", "Secret", "sec-openai");

        Assert.Equal(2, resolution.Resource!.Version);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ResolveAsync_MissingOrWrongTypeTarget_ReportsWhy(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedTargetsAsync(provider);
        var resolver = provider.GetRequiredService<IResourceReferenceResolver>();

        var missing = await resolver.ResolveAsync(AgentDefinition(), "Links", "Secret", "sec-missing");
        var wrongType = await resolver.ResolveAsync(AgentDefinition(), "Links", "Secret", "tool-search");
        var allowedArrayTarget = await resolver.ResolveAsync(AgentDefinition(), "Links", "Tools", "tool-search");

        Assert.Equal(ResourceReferenceResolutionStatus.TargetNotFound, missing.Status);
        Assert.Equal(ResourceReferenceResolutionStatus.TargetTypeMismatch, wrongType.Status);
        Assert.Equal("Tool", wrongType.Resource!.DefinitionId);
        Assert.True(allowedArrayTarget.IsResolved);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ResolveAsync_IsTenantScoped(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var tenantA = TenantScope.FromTenantId("tenant-a");
        var tenantB = TenantScope.FromTenantId("tenant-b");
        await SeedTargetsAsync(provider, tenantA);
        var resolver = provider.GetRequiredService<IResourceReferenceResolver>();

        var inTenantA = await resolver.ResolveAsync(AgentDefinition(), "Links", "Secret", "sec-openai", tenantA);
        var inTenantB = await resolver.ResolveAsync(AgentDefinition(), "Links", "Secret", "sec-openai", tenantB);

        Assert.True(inTenantA.IsResolved);
        Assert.Equal(ResourceReferenceResolutionStatus.TargetNotFound, inTenantB.Status);
    }

    [Theory]
    [InlineData("Missing", "Secret")]
    [InlineData("Links", "Missing")]
    [InlineData("Links", "Label")]
    public async Task ResolveAsync_NotAReferenceFacet_Throws(string aspectKey, string facetId)
    {
        await using var provider = CreateProvider(InMemory);
        var definition = Definition("Agent", Facet("Secret", "reference"), Facet("Label", "string"));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await provider.GetRequiredService<IResourceReferenceResolver>().ResolveAsync(definition, aspectKey, facetId, "sec-openai"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Portability
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Export_IncludesReferenceMetadata(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(AgentDefinition(resolution: "production"));

        var export = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.DefinitionsOnly,
            DefinitionIds = ["Agent"],
        });

        var facet = export.Snapshot!.Definitions.Single().AspectDefinitions["Links"].FacetDefinitions[0];
        Assert.Equal("reference", facet.Type);
        Assert.Equal("production", facet.Metadata!.ReferenceResolution);
        Assert.Equal(["Secret"], facet.Metadata.TargetDefinitionIds!);
    }
}
