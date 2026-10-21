using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

/// <summary>
/// #90: archive and soft-delete policy previews, and lifecycle restore previews, report each resource
/// that references a candidate as <c>has-inbound-references</c>, deterministically and identically for both
/// providers; candidates without referrers get no new diagnostics.
/// </summary>
public sealed class InboundReferenceDiagnosticsTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private static readonly DateTimeOffset EvaluationTimestamp = DateTimeOffset.UtcNow.AddDays(1);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static FacetDefinition Facet(string id, string type) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
    };

    private static ResourceDefinition Definition(
        string definitionId,
        string aspectKey,
        IReadOnlyList<ResourcePolicyDeclaration> policies,
        params FacetDefinition[] facets) => new()
    {
        DefinitionId = definitionId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        PolicyDeclarations = policies,
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

    /// <summary>
    /// Agents agent-a, agent-b, agent-lonely (archive policy); workflows: workflow-2 → agent-a,
    /// workflow-1 → agent-a (twice) and agent-b; agent-b references itself.
    /// </summary>
    private static async Task SeedAsync(ServiceProvider provider)
    {
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(Definition(
            "Agent",
            "Profile",
            [PolicyTestFixtures.ArchivePolicy(minimumAge: TimeSpan.FromSeconds(1))],
            Facet("Name", "string"),
            Facet("Self", "reference")));
        await definitions.RegisterDefinitionAsync(Definition(
            "Workflow",
            "Steps",
            [],
            Facet("Primary", "reference"),
            Facet("Agents", "reference[]")));

        var manager = provider.GetRequiredService<IResourceManager>();
        foreach (var agentId in new[] { "agent-a", "agent-b", "agent-lonely" })
            await manager.CreateAsync("Agent", new CreateResourceRequest { ResourceId = agentId });

        await manager.UpdateAsync("agent-b", new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = new Dictionary<string, object> { ["Profile"] = new Dictionary<string, object> { ["Self"] = "agent-b" } },
        });
        await manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            ResourceId = "workflow-2",
            InitialAspects = new Dictionary<string, object> { ["Steps"] = new Dictionary<string, object> { ["Primary"] = "agent-a" } },
        });
        await manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            ResourceId = "workflow-1",
            InitialAspects = new Dictionary<string, object>
            {
                ["Steps"] = new Dictionary<string, object> { ["Primary"] = "agent-a", ["Agents"] = new[] { "agent-b", "agent-a" } },
            },
        });
    }

    private static (string? ResourceId, string? Path)[] Inbound(IEnumerable<ResourcePolicyDiagnostic> diagnostics) =>
        diagnostics
            .Where(static diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.HasInboundReferences)
            .Select(static diagnostic => (diagnostic.ResourceId, diagnostic.Path))
            .ToArray();

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ArchivePreview_ReportsEachReferrerOfEachCandidate(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = EvaluationTimestamp,
        });

        Assert.Equal(3, preview.Candidates.Count(static candidate => candidate.Outcome == ResourcePolicyOutcome.Archive));
        var inbound = preview.Diagnostics.Where(static d => d.Code == ResourcePolicyDiagnosticCodes.HasInboundReferences).ToList();
        Assert.Equal(
            new (string?, string?)[]
            {
                ("agent-a", "referrers/workflow-1"),
                ("agent-a", "referrers/workflow-2"),
                ("agent-b", "referrers/workflow-1"),
            },
            Inbound(inbound));
        Assert.All(inbound, static diagnostic => Assert.Equal("archive-old", diagnostic.PolicyId));
        Assert.Equal(2, inbound.Single(static d => d.ResourceId == "agent-b").ResourceVersion);
        Assert.Equal("Resource 'agent-a' is referenced by 'workflow-1'.", inbound[0].Message);
    }

    [Fact]
    public async Task ArchivePreview_DiagnosticsAreIdenticalAcrossProviders()
    {
        await using var inMemory = CreateProvider(InMemory);
        await using var sqlite = CreateProvider(Sqlite);
        await SeedAsync(inMemory);
        await SeedAsync(sqlite);
        var request = new ResourcePolicyEvaluationRequest { EvaluationTimestamp = EvaluationTimestamp };

        var first = await inMemory.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(request);
        var second = await sqlite.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(request);

        Assert.Equal(Inbound(first.Diagnostics), Inbound(second.Diagnostics));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Preview_WithoutReferences_HasNoNewDiagnostics(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);

        var preview = await provider.GetRequiredService<IResourcePolicyEvaluationService>().PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            EvaluationTimestamp = EvaluationTimestamp,
            DefinitionIds = ["Agent"],
        });

        Assert.Contains(preview.Candidates, static candidate => candidate.ResourceId == "agent-lonely");
        Assert.DoesNotContain(preview.Diagnostics, static diagnostic => diagnostic.ResourceId == "agent-lonely");
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RestorePreview_ReportsReferrersOfRestorableCandidates(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);
        var markers = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        foreach (var resourceId in new[] { "agent-a", "agent-lonely" })
        {
            await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                ResourceId = resourceId,
                State = ResourceLifecycleMarkerState.Archived,
                MarkedAt = DateTimeOffset.UtcNow,
            });
        }

        var preview = await provider.GetRequiredService<IResourceLifecycleRestoreService>().PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                new ResourceLifecycleRestoreCandidate { ResourceId = "agent-a", ExpectedState = ResourceLifecycleMarkerState.Archived },
                new ResourceLifecycleRestoreCandidate { ResourceId = "agent-lonely", ExpectedState = ResourceLifecycleMarkerState.Archived },
                new ResourceLifecycleRestoreCandidate { ResourceId = "agent-b", ExpectedState = ResourceLifecycleMarkerState.Archived },
            ],
        });

        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Restorable, preview.Candidates[0].Status);
        Assert.Equal(
            new (string?, string?)[] { ("agent-a", "referrers/workflow-1"), ("agent-a", "referrers/workflow-2") },
            Inbound(preview.Candidates[0].Diagnostics));
        Assert.Empty(preview.Candidates[1].Diagnostics);
        Assert.Empty(Inbound(preview.Candidates[2].Diagnostics));
        Assert.Equal(2, preview.RestorableCount);
    }
}
