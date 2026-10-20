using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.References;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

/// <summary>
/// #89: import rewrites reference values whose targets are remapped and records the rewrites in the
/// identity map; it reports dangling references by reference strictness; export can add referenced
/// resources transitively. Covered for both providers.
/// </summary>
public sealed class ReferencePortabilityTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly List<string> databasePaths = [];

    public void Dispose()
    {
        foreach (var path in databasePaths)
            SqliteJsonTestDatabase.DeleteFiles(path);
    }

    private ServiceProvider CreateProvider(string providerKind, ReferenceValidationMode mode = ReferenceValidationMode.Strict)
    {
        var services = new ServiceCollection().AddAsterCore(options => options.ReferenceValidation = mode);
        if (providerKind == Sqlite)
        {
            var path = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");
            databasePaths.Add(path);
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={path}");
        }

        return services.BuildServiceProvider();
    }

    private static FacetDefinition Facet(string id, string type) => new()
    {
        FacetDefinitionId = id,
        Id = $"facet-{id}",
        Version = 1,
        Type = type,
    };

    private static ResourceDefinition Definition(string definitionId, string aspectKey, params FacetDefinition[] facets) => new()
    {
        DefinitionId = definitionId,
        Id = $"definition-{definitionId}",
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            [aspectKey] = new()
            {
                AspectDefinitionId = aspectKey,
                Id = $"aspect-{aspectKey}",
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static readonly ResourceDefinition AgentDefinition =
        Definition("Agent", "Profile", Facet("Name", "string"), Facet("Backup", "reference"));

    private static readonly ResourceDefinition WorkflowDefinition =
        Definition("Workflow", "Steps", Facet("Primary", "reference"), Facet("Agents", "reference[]"));

    private static async Task RegisterAsync(ServiceProvider provider)
    {
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(AgentDefinition);
        await definitions.RegisterDefinitionAsync(WorkflowDefinition);
    }

    private static Task<Resource> CreateAgentAsync(IResourceManager manager, string resourceId, string name, string? backup = null)
    {
        var profile = new Dictionary<string, object> { ["Name"] = name };
        if (backup is not null)
            profile["Backup"] = backup;

        return manager.CreateAsync("Agent", new CreateResourceRequest
        {
            ResourceId = resourceId,
            InitialAspects = new Dictionary<string, object> { ["Profile"] = profile },
        }).AsTask();
    }

    /// <summary>agent-c; agent-b (Backup → agent-c); agent-a; workflow-1 (Primary → agent-a, Agents → [agent-b, agent-a]).</summary>
    private static async Task<IResourceManager> SeedSourceAsync(ServiceProvider provider)
    {
        await RegisterAsync(provider);
        var manager = provider.GetRequiredService<IResourceManager>();
        await CreateAgentAsync(manager, "agent-c", "Charlie");
        await CreateAgentAsync(manager, "agent-b", "Bravo", backup: "agent-c");
        await CreateAgentAsync(manager, "agent-a", "Alpha");
        await manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            ResourceId = "workflow-1",
            InitialAspects = new Dictionary<string, object>
            {
                ["Steps"] = new Dictionary<string, object> { ["Primary"] = "agent-a", ["Agents"] = new[] { "agent-b", "agent-a" } },
            },
        });

        return manager;
    }

    private static async Task<PortableSnapshot> ExportAsync(ServiceProvider provider, PortableSnapshotExportRequest request)
    {
        var result = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(request);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error);
        return result.Snapshot!;
    }

    private static Task<PortableSnapshot> ExportAllAsync(ServiceProvider provider) => ExportAsync(provider, new PortableSnapshotExportRequest
    {
        ScopeMode = PortableExportScopeMode.DefinitionWithResources,
        DefinitionIds = ["Agent", "Workflow"],
    });

    private static string Text(object payload, string property) =>
        JsonSerializer.SerializeToElement(payload).GetProperty(property).GetString()!;

    private static string[] Texts(object payload, string property) =>
        JsonSerializer.SerializeToElement(payload).GetProperty(property).EnumerateArray().Select(static element => element.GetString()!).ToArray();

    // ──────────────────────────────────────────────────────────────────────────
    // Remapping
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task RemappingImport_RewritesReferencesToTheRemappedTarget(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        await SeedSourceAsync(source);
        var snapshot = await ExportAllAsync(source);

        await using var target = CreateProvider(providerKind);
        await RegisterAsync(target);
        await CreateAgentAsync(target.GetRequiredService<IResourceManager>(), "agent-a", "A different agent");
        var portability = target.GetRequiredService<IResourcePortabilityService>();
        var options = new PortableImportOptions { CollisionMode = PortableImportCollisionMode.RemapDivergent };

        var preview = await portability.PreviewImportAsync(snapshot, options);
        // Resource version identities are JSON arrays: ["resourceId", version].
        var remappedAgentId = JsonDocument.Parse(Assert.Single(preview.IdentityMap, static mapping =>
                mapping.EntityKind == PortableEntityKind.ResourceVersion && mapping.SourceId == "[\"agent-a\",1]").TargetId)
            .RootElement[0].GetString()!;
        Assert.NotEqual("agent-a", remappedAgentId);
        Assert.True(preview.CanImport);
        Assert.Equal(
            new[]
            {
                ("resources/workflow-1/1/Steps/Agents/1", "agent-a", remappedAgentId),
                ("resources/workflow-1/1/Steps/Primary", "agent-a", remappedAgentId),
            },
            preview.IdentityMap
                .Where(static mapping => mapping.EntityKind == PortableEntityKind.ReferenceValue)
                .Select(static mapping => (mapping.Location!, mapping.SourceId, mapping.TargetId))
                .ToArray());
        Assert.All(
            preview.IdentityMap.Where(static mapping => mapping.EntityKind == PortableEntityKind.ReferenceValue),
            static mapping => Assert.Equal(PortableIdentityMappingReason.RemappedDivergent, mapping.Reason));
        Assert.Equal(1, preview.Counts.RemappedItems);

        var result = await portability.ImportAsync(snapshot, options);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        var workflow = await target.GetRequiredService<IResourceManager>().GetLatestVersionAsync("workflow-1");
        Assert.Equal(remappedAgentId, Text(workflow!.Aspects["Steps"], "Primary"));
        Assert.Equal(new[] { "agent-b", remappedAgentId }, Texts(workflow.Aspects["Steps"], "Agents"));

        var references = target.GetRequiredService<IResourceReferenceService>();
        Assert.Equal("workflow-1", Assert.Single((await references.GetReferrersAsync(remappedAgentId)).Items).Resource.ResourceId);
        Assert.Empty((await references.GetReferrersAsync("agent-a")).Items);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ImportWithoutRemapping_LeavesReferencesAndReportsNoRewrites(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        await SeedSourceAsync(source);
        var snapshot = await ExportAllAsync(source);
        await using var target = CreateProvider(providerKind);

        var result = await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(snapshot);

        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.DoesNotContain(result.IdentityMap, static mapping => mapping.EntityKind == PortableEntityKind.ReferenceValue);
        Assert.Empty(result.Diagnostics);
        var workflow = await target.GetRequiredService<IResourceManager>().GetLatestVersionAsync("workflow-1");
        Assert.Equal("agent-a", Text(workflow!.Aspects["Steps"], "Primary"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dangling references
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory, ReferenceValidationMode.Strict)]
    [InlineData(InMemory, ReferenceValidationMode.Lenient)]
    [InlineData(Sqlite, ReferenceValidationMode.Strict)]
    [InlineData(Sqlite, ReferenceValidationMode.Lenient)]
    public async Task Import_ReferenceOutsideSnapshotAndTarget_IsDangling(string providerKind, ReferenceValidationMode mode)
    {
        await using var source = CreateProvider(providerKind);
        await SeedSourceAsync(source);
        var snapshot = await ExportAsync(source, new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["workflow-1", "agent-b"],
        });

        await using var target = CreateProvider(providerKind, mode);
        var result = await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(snapshot);

        var expectedSeverity = mode == ReferenceValidationMode.Strict ? PortableDiagnosticSeverity.Error : PortableDiagnosticSeverity.Warning;
        Assert.Equal(
            new[]
            {
                ("resources/agent-b/1/Profile/Backup", expectedSeverity),
                ("resources/workflow-1/1/Steps/Agents/1", expectedSeverity),
                ("resources/workflow-1/1/Steps/Primary", expectedSeverity),
            },
            result.Diagnostics
                .Where(static diagnostic => diagnostic.Code == PortableDiagnosticCodes.DanglingReference)
                .Select(static diagnostic => (diagnostic.Path!, diagnostic.Severity))
                .OrderBy(static entry => entry.Item1, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(mode == ReferenceValidationMode.Strict ? PortableImportStatus.Failed : PortableImportStatus.Imported, result.Status);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Import_ReferenceToResourceAlreadyInTarget_IsNotDangling(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        await SeedSourceAsync(source);
        var snapshot = await ExportAsync(source, new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["workflow-1"],
        });

        await using var target = CreateProvider(providerKind);
        await RegisterAsync(target);
        var manager = target.GetRequiredService<IResourceManager>();
        await CreateAgentAsync(manager, "agent-a", "Alpha");
        await CreateAgentAsync(manager, "agent-b", "Bravo");

        var preview = await target.GetRequiredService<IResourcePortabilityService>().PreviewImportAsync(snapshot);

        Assert.True(preview.CanImport);
        Assert.DoesNotContain(preview.Diagnostics, static diagnostic => diagnostic.Code == PortableDiagnosticCodes.DanglingReference);
    }

    [Fact]
    public async Task Import_DanglingDiagnosticsAreDeterministic()
    {
        await using var source = CreateProvider(InMemory);
        await SeedSourceAsync(source);
        var snapshot = await ExportAsync(source, new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["workflow-1"],
        });

        await using var first = CreateProvider(InMemory);
        await using var second = CreateProvider(Sqlite);
        var firstPaths = (await first.GetRequiredService<IResourcePortabilityService>().PreviewImportAsync(snapshot)).Diagnostics.Select(static d => d.Path);
        var secondPaths = (await second.GetRequiredService<IResourcePortabilityService>().PreviewImportAsync(snapshot)).Diagnostics.Select(static d => d.Path);

        Assert.Equal(new[] { "resources/workflow-1/1/Steps/Agents/0", "resources/workflow-1/1/Steps/Agents/1", "resources/workflow-1/1/Steps/Primary" }, firstPaths);
        Assert.Equal(firstPaths, secondPaths);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Export with dependencies
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ExportWithReferencedResources_AddsTheTransitiveClosure(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        await SeedSourceAsync(source);

        var snapshot = await ExportAsync(source, new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["workflow-1"],
            IncludeReferencedResources = true,
        });

        Assert.Equal(
            new[] { "agent-a", "agent-b", "agent-c", "workflow-1" },
            snapshot.Resources.Select(static resource => resource.ResourceId).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "Agent", "Workflow" },
            snapshot.Definitions.Select(static definition => definition.DefinitionId).Distinct().Order(StringComparer.Ordinal).ToArray());

        await using var target = CreateProvider(providerKind);
        var result = await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(snapshot);
        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ExportWithoutReferencedResources_ExportsOnlyTheSelection(string providerKind)
    {
        await using var source = CreateProvider(providerKind);
        await SeedSourceAsync(source);

        var snapshot = await ExportAsync(source, new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["workflow-1"],
        });

        Assert.Equal("workflow-1", Assert.Single(snapshot.Resources).ResourceId);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task ExportWithReferencedResources_MissingTarget_StopsWithoutLooping(string providerKind)
    {
        await using var source = CreateProvider(providerKind, ReferenceValidationMode.Lenient);
        await RegisterAsync(source);
        await CreateAgentAsync(source.GetRequiredService<IResourceManager>(), "agent-x", "X", backup: "gone");

        var snapshot = await ExportAsync(source, new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["agent-x"],
            IncludeReferencedResources = true,
        });

        Assert.Equal("agent-x", Assert.Single(snapshot.Resources).ResourceId);
    }
}
