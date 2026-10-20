using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// #88: forward reference filters (<see cref="ReferenceQuery"/>, <c>TypedFacetQuery.References</c>) and
/// reverse lookup (<see cref="IResourceReferenceService"/>) return the same results from the in-memory
/// and SQLite providers, scoped to one tenant.
/// </summary>
public sealed class ReferenceLookupTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Published = "published";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind, ReferenceValidationMode mode = ReferenceValidationMode.Strict)
    {
        var services = new ServiceCollection().AddAsterCore(options => options.ReferenceValidation = mode);
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

    private static ResourceDefinition Definition(string definitionId, string aspectKey, params FacetDefinition[] facets) => new()
    {
        DefinitionId = definitionId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            [aspectKey] = new()
            {
                AspectDefinitionId = aspectKey,
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static Dictionary<string, object> Aspect(string aspectKey, Dictionary<string, object> values) => new() { [aspectKey] = values };

    /// <summary>
    /// Agents agent-a and Agent-A (ordinally distinct). Workflows reference agents through Steps.Primary
    /// (reference) and Steps.Agents (reference[]); a Note mentions agent-a in a plain string facet.
    /// </summary>
    private static async Task<IResourceManager> SeedAsync(ServiceProvider provider, TenantScope? tenant = null)
    {
        tenant ??= TenantScope.Default;
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(Definition("Agent", "Profile", Facet("Name", "string")), tenant, CancellationToken.None);
        await definitions.RegisterDefinitionAsync(Definition(
            "Workflow",
            "Steps",
            Facet("Primary", "reference", new FacetMetadata { TargetDefinitionIds = ["Agent"] }),
            Facet("Agents", "reference[]", new FacetMetadata { TargetDefinitionIds = ["Agent"] })), tenant, CancellationToken.None);
        await definitions.RegisterDefinitionAsync(Definition("Note", "Details", Facet("Mention", "string")), tenant, CancellationToken.None);

        var manager = provider.GetRequiredService<IResourceManager>();
        foreach (var agentId in new[] { "agent-a", "Agent-A", "agent-b" })
            await manager.CreateAsync("Agent", new CreateResourceRequest { ResourceId = agentId, TenantScope = tenant });

        await CreateWorkflowAsync(manager, "workflow-2", tenant, primary: null, "agent-a");
        await CreateWorkflowAsync(manager, "workflow-1", tenant, primary: "agent-a", "agent-b", "agent-a", "agent-a");
        await CreateWorkflowAsync(manager, "workflow-3", tenant, primary: "agent-b");
        await CreateWorkflowAsync(manager, "workflow-4", tenant, primary: "Agent-A");
        await manager.CreateAsync("Note", new CreateResourceRequest
        {
            ResourceId = "note-1",
            TenantScope = tenant,
            InitialAspects = Aspect("Details", new Dictionary<string, object> { ["Mention"] = "agent-a" }),
        });

        return manager;
    }

    private static Task<Resource> CreateWorkflowAsync(IResourceManager manager, string resourceId, TenantScope tenant, string? primary, params string[] agents)
    {
        var steps = new Dictionary<string, object>();
        if (primary is not null)
            steps["Primary"] = primary;
        if (agents.Length > 0)
            steps["Agents"] = agents;

        return manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            ResourceId = resourceId,
            TenantScope = tenant,
            InitialAspects = Aspect("Steps", steps),
        }).AsTask();
    }

    private static async Task<string[]> QueryIdsAsync(ServiceProvider provider, FilterExpression filter, TenantScope? tenant = null) =>
        (await provider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            TenantScope = tenant,
            Filter = filter,
            Sorts = [new SortExpression("ResourceId")],
        })).Select(static resource => resource.ResourceId).ToArray();

    private static string[] Ids(ResourceReferrerPage page) =>
        page.Items.Select(static referrer => referrer.Resource.ResourceId).ToArray();

    // ──────────────────────────────────────────────────────────────────────────
    // Forward filters
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ReferenceQueryTargets_MatchesScalarAndArrayReferences(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);

        // Providers compare text case-insensitively, so workflow-4's "Agent-A" matches too (documented on Targets).
        Assert.Equal(new[] { "workflow-1", "workflow-4" }, await QueryIdsAsync(provider, ReferenceQuery.Targets("Steps", "Primary", "agent-a")));
        Assert.Equal(new[] { "workflow-1", "workflow-2" }, await QueryIdsAsync(provider, ReferenceQuery.Targets("Steps", "Agents", "agent-a")));
        Assert.Equal(new[] { "workflow-1" }, await QueryIdsAsync(provider, ReferenceQuery.Targets("Steps", "Agents", "agent-b")));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task TypedFacetQueryReferences_MatchesLikeReferenceQueryTargets(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);
        var steps = TypedQuery.For<WorkflowSteps>("Steps");

        Assert.Equal(new[] { "workflow-1", "workflow-3" }, await QueryIdsAsync(provider, TypedQuery.Or(
            steps.Facet(static aspect => aspect.Primary).References("agent-b"),
            steps.Facet(static aspect => aspect.Agents).References("agent-b"))));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reverse lookup
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_ReturnsReferringResourcesWithEveryLocation(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);

        var page = await provider.GetRequiredService<IResourceReferenceService>().GetReferrersAsync("agent-a");

        Assert.False(page.HasMore);
        Assert.Equal(new[] { "workflow-1", "workflow-2" }, Ids(page));
        Assert.Equal(
            new[]
            {
                new ResourceReferenceLocation("Steps", "Agents", 1),
                new ResourceReferenceLocation("Steps", "Agents", 2),
                new ResourceReferenceLocation("Steps", "Primary", null),
            },
            page.Items[0].Locations);
        Assert.Equal(new[] { new ResourceReferenceLocation("Steps", "Agents", 0) }, page.Items[1].Locations);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_MatchesResourceIdsOrdinally(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);
        var references = provider.GetRequiredService<IResourceReferenceService>();

        Assert.Equal(new[] { "workflow-4" }, Ids(await references.GetReferrersAsync("Agent-A")));
        Assert.Empty((await references.GetReferrersAsync("AGENT-A")).Items);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_UnknownTargetOrNoReferenceFacets_ReturnsEmpty(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);
        var references = provider.GetRequiredService<IResourceReferenceService>();

        Assert.Empty((await references.GetReferrersAsync("nobody")).Items);
        Assert.Empty((await references.GetReferrersAsync("agent-a", new ResourceReferrerQuery { DefinitionId = "Note" })).Items);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_PagesInResourceIdOrder(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);
        var references = provider.GetRequiredService<IResourceReferenceService>();

        var first = await references.GetReferrersAsync("agent-b", new ResourceReferrerQuery { Take = 1 });
        var second = await references.GetReferrersAsync("agent-b", new ResourceReferrerQuery { Skip = 1, Take = 1 });
        var none = await references.GetReferrersAsync("agent-b", new ResourceReferrerQuery { Take = 0 });

        Assert.Equal(new[] { "workflow-1" }, Ids(first));
        Assert.True(first.HasMore);
        Assert.Equal(new[] { "workflow-3" }, Ids(second));
        Assert.False(second.HasMore);
        Assert.Empty(none.Items);
        Assert.False(none.HasMore);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_IsTenantScoped(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var tenantA = TenantScope.FromTenantId("tenant-a");
        var tenantB = TenantScope.FromTenantId("tenant-b");
        await SeedAsync(provider, tenantA);
        var manager = await SeedAsync(provider, tenantB);
        await CreateWorkflowAsync(manager, "workflow-b-only", tenantB, primary: "agent-a");
        var references = provider.GetRequiredService<IResourceReferenceService>();

        Assert.Equal(new[] { "workflow-1", "workflow-2" }, Ids(await references.GetReferrersAsync("agent-a", new ResourceReferrerQuery { TenantScope = tenantA })));
        Assert.Equal(
            new[] { "workflow-1", "workflow-2", "workflow-b-only" },
            Ids(await references.GetReferrersAsync("agent-a", new ResourceReferrerQuery { TenantScope = tenantB })));
        Assert.Empty((await references.GetReferrersAsync("agent-a")).Items);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_ActiveScope_SearchesTheChannelsVersions(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await SeedAsync(provider);
        await manager.ActivateAsync("workflow-3", 1, Published);
        await manager.UpdateAsync("workflow-3", new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = Aspect("Steps", new Dictionary<string, object> { ["Primary"] = "agent-a" }),
        });
        var references = provider.GetRequiredService<IResourceReferenceService>();

        Assert.Equal(new[] { "workflow-1" }, Ids(await references.GetReferrersAsync("agent-b")));
        var active = await references.GetReferrersAsync("agent-b", new ResourceReferrerQuery
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = Published,
        });
        Assert.Equal(1, Assert.Single(active.Items).Resource.Version);
        Assert.Equal("workflow-3", active.Items[0].Resource.ResourceId);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task GetReferrers_FindsLenientlySavedReferencesOfAnyDefinition(string providerKind)
    {
        await using var provider = CreateProvider(providerKind, ReferenceValidationMode.Lenient);
        var manager = await SeedAsync(provider);
        await CreateWorkflowAsync(manager, "workflow-odd", TenantScope.Default, primary: "note-1");

        var page = await provider.GetRequiredService<IResourceReferenceService>().GetReferrersAsync("note-1");

        Assert.Equal(new[] { "workflow-odd" }, Ids(page));
    }

    [Fact]
    public async Task GetReferrers_ProviderWithoutSupport_Throws()
    {
        await using var provider = CreateProvider(InMemory);
        var service = new ResourceReferenceService(
            provider.GetRequiredService<IResourceDefinitionStore>(),
            provider.GetRequiredService<IResourceQueryService>(),
            new FixedCapabilities(provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities with
            {
                SupportsReverseReferenceLookup = false,
            }));

        await Assert.ThrowsAsync<NotSupportedException>(async () => await service.GetReferrersAsync("agent-a"));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public void Capabilities_DeclareReverseLookup(string providerKind)
    {
        using var provider = CreateProvider(providerKind);

        Assert.True(provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities.SupportsReverseReferenceLookup);
    }

    private sealed class WorkflowSteps
    {
        public string? Primary { get; init; }

        public string[] Agents { get; init; } = [];
    }

    private sealed class FixedCapabilities(QueryCapabilityDescription capabilities) : IResourceQueryCapabilitiesProvider
    {
        public QueryCapabilityDescription Capabilities { get; } = capabilities;
    }
}
