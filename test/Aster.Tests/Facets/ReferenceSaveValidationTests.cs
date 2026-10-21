using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Facets;

/// <summary>
/// #87: saves validate reference targets (existence in the tenant, TargetDefinitionIds, the
/// ReferenceResolution channel) with bounded reads; strict mode rejects the save, lenient mode saves
/// and hands the diagnostics to after-save hooks, identically for both providers.
/// </summary>
public sealed class ReferenceSaveValidationTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Published = "published";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    public static TheoryData<string, ReferenceValidationMode> ProvidersAndModes => new()
    {
        { InMemory, ReferenceValidationMode.Strict },
        { InMemory, ReferenceValidationMode.Lenient },
        { Sqlite, ReferenceValidationMode.Strict },
        { Sqlite, ReferenceValidationMode.Lenient },
    };

    private ServiceProvider CreateProvider(string providerKind, ReferenceValidationMode mode = ReferenceValidationMode.Strict)
    {
        var services = new ServiceCollection()
            .AddAsterCore(options => options.ReferenceValidation = mode)
            .AddResourceLifecycleHook<WarningRecordingHook>();

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

    private static AspectDefinition Aspect(string id, params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        FacetDefinitions = facets,
    };

    private static ResourceDefinition Definition(string definitionId, params AspectDefinition[] aspects) => new()
    {
        DefinitionId = definitionId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = aspects.ToDictionary(static aspect => aspect.AspectDefinitionId),
    };

    /// <summary>
    /// Agent.Links: Secret (reference to a Secret, resolved through the published channel) and Tools
    /// (reference[] to Tool or Workflow, latest). Agent.Details: Name (string).
    /// </summary>
    private static ResourceDefinition AgentDefinition() => Definition(
        "Agent",
        Aspect(
            "Links",
            Facet("Secret", "reference", new FacetMetadata { TargetDefinitionIds = ["Secret"], ReferenceResolution = Published }),
            Facet("Tools", "reference[]", new FacetMetadata { TargetDefinitionIds = ["Tool", "Workflow"] })),
        Aspect("Details", Facet("Name", "string")));

    private static Dictionary<string, object> Links(string? secret = null, params string[] tools)
    {
        var links = new Dictionary<string, object>();
        if (secret is not null)
            links["Secret"] = secret;
        if (tools.Length > 0)
            links["Tools"] = tools;
        return links;
    }

    private static async Task<IResourceManager> SeedAsync(ServiceProvider provider, TenantScope? tenant = null)
    {
        tenant ??= TenantScope.Default;
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        foreach (var definitionId in new[] { "Secret", "Tool", "Note" })
            await definitions.RegisterDefinitionAsync(Definition(definitionId, Aspect("Details", Facet("Name", "string"))), tenant, CancellationToken.None);
        await definitions.RegisterDefinitionAsync(AgentDefinition(), tenant, CancellationToken.None);

        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.CreateAsync("Secret", new CreateResourceRequest { ResourceId = "sec-live", TenantScope = tenant });
        await manager.CreateAsync("Secret", new CreateResourceRequest { ResourceId = "sec-draft", TenantScope = tenant });
        await manager.CreateAsync("Tool", new CreateResourceRequest { ResourceId = "tool-search", TenantScope = tenant });
        await manager.CreateAsync("Note", new CreateResourceRequest { ResourceId = "note-1", TenantScope = tenant });
        await manager.ActivateAsync("sec-live", 1, Published, tenant, allowMultipleActive: false, CancellationToken.None);
        return manager;
    }

    private static Task<Resource> CreateAgentAsync(IResourceManager manager, Dictionary<string, object> links, TenantScope? tenant = null) =>
        manager.CreateAsync("Agent", new CreateResourceRequest
        {
            ResourceId = "agent-1",
            TenantScope = tenant,
            InitialAspects = new Dictionary<string, object> { ["Links"] = links },
        }).AsTask();

    /// <summary>
    /// Saves the agent in the given mode: strict must throw and persist nothing; lenient must save and
    /// pass the same diagnostics to the after-save hook. Returns the diagnostics either way.
    /// </summary>
    private static async Task<IReadOnlyList<FacetValueDiagnostic>> SaveRejectedOrWarnedAsync(
        ServiceProvider provider,
        ReferenceValidationMode mode,
        Func<Task<Resource>> save,
        string resourceId = "agent-1",
        int expectedVersionIfSaved = 1)
    {
        var manager = provider.GetRequiredService<IResourceManager>();
        var hook = provider.GetRequiredService<WarningRecordingHook>();

        if (mode == ReferenceValidationMode.Strict)
        {
            var before = await manager.GetLatestVersionAsync(resourceId);
            var exception = await Assert.ThrowsAsync<FacetValueValidationException>(save);
            Assert.Equal(before?.Version, (await manager.GetLatestVersionAsync(resourceId))?.Version);
            Assert.Empty(hook.AfterSaveWarnings);
            return exception.Diagnostics;
        }

        var saved = await save();
        Assert.Equal(expectedVersionIfSaved, saved.Version);
        return Assert.Single(hook.AfterSaveWarnings);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rules, in both modes
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Save_ValidReferences_SavesWithoutDiagnostics(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var manager = await SeedAsync(provider);

        var saved = await CreateAgentAsync(manager, Links("sec-live", "tool-search"));

        Assert.Equal(1, saved.Version);
        Assert.Empty(Assert.Single(provider.GetRequiredService<WarningRecordingHook>().AfterSaveWarnings));
    }

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Save_MissingTarget_ReportsTargetMissing(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var manager = await SeedAsync(provider);

        var diagnostics = await SaveRejectedOrWarnedAsync(provider, mode, () => CreateAgentAsync(manager, Links("sec-gone")));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetMissing, diagnostic.Code);
        Assert.Equal("Links", diagnostic.AspectKey);
        Assert.Equal("Secret", diagnostic.FacetDefinitionId);
        Assert.Null(diagnostic.ElementIndex);
        Assert.Contains("sec-gone", diagnostic.Message);
    }

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Save_TargetOfWrongDefinition_ReportsTypeMismatchWithElementIndex(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var manager = await SeedAsync(provider);

        var diagnostics = await SaveRejectedOrWarnedAsync(provider, mode,
            () => CreateAgentAsync(manager, Links(tools: ["tool-search", "note-1"])));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch, diagnostic.Code);
        Assert.Equal("Links", diagnostic.AspectKey);
        Assert.Equal("Tools", diagnostic.FacetDefinitionId);
        Assert.Equal(1, diagnostic.ElementIndex);
        Assert.StartsWith("Element [1]: ", diagnostic.Message);
        Assert.Contains("'Note'", diagnostic.Message);
    }

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Save_TargetNotActiveInPinnedChannel_ReportsPinMissing(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var manager = await SeedAsync(provider);

        var diagnostics = await SaveRejectedOrWarnedAsync(provider, mode, () => CreateAgentAsync(manager, Links("sec-draft")));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.ReferencePinMissing, diagnostic.Code);
        Assert.Equal("Secret", diagnostic.FacetDefinitionId);
        Assert.Contains(Published, diagnostic.Message);
    }

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Save_SeveralViolations_ReportsEachInStableOrder(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var manager = await SeedAsync(provider);

        var diagnostics = await SaveRejectedOrWarnedAsync(provider, mode,
            () => CreateAgentAsync(manager, Links("sec-draft", "missing-tool", "tool-search", "sec-live")));

        Assert.Equal(
            new[]
            {
                (FacetValueDiagnosticCodes.ReferencePinMissing, "Secret", (int?)null),
                (FacetValueDiagnosticCodes.ReferenceTargetMissing, "Tools", 0),
                (FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch, "Tools", 2),
            },
            diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.FacetDefinitionId, diagnostic.ElementIndex)).ToArray());
    }

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Save_TargetInAnotherTenantOnly_ReportsTargetMissing(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var tenantA = TenantScope.FromTenantId("tenant-a");
        var tenantB = TenantScope.FromTenantId("tenant-b");
        await SeedAsync(provider, tenantA);
        var manager = await SeedAsync(provider, tenantB);
        await manager.CreateAsync("Tool", new CreateResourceRequest { ResourceId = "tool-a-only", TenantScope = tenantA });

        var diagnostics = await SaveRejectedOrWarnedAsync(provider, mode,
            () => CreateAgentAsync(manager, Links(tools: ["tool-a-only"]), tenantB));

        Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetMissing, Assert.Single(diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(ProvidersAndModes))]
    public async Task Update_ValidatesOnlyTheAspectsItWrites(string providerKind, ReferenceValidationMode mode)
    {
        await using var provider = CreateProvider(providerKind, mode);
        var manager = await SeedAsync(provider);
        await CreateAgentAsync(manager, Links("sec-live"));
        await manager.DeactivateAsync("sec-live", 1, Published);
        var hook = provider.GetRequiredService<WarningRecordingHook>();
        hook.AfterSaveWarnings.Clear();

        // The now-unpinned Secret sits in Links; an update that writes only Details is not blocked by it.
        var renamed = await manager.UpdateAsync("agent-1", new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = new Dictionary<string, object> { ["Details"] = new Dictionary<string, object> { ["Name"] = "Researcher" } },
        });
        Assert.Equal(2, renamed.Version);
        Assert.Empty(Assert.Single(hook.AfterSaveWarnings));
        hook.AfterSaveWarnings.Clear();

        var diagnostics = await SaveRejectedOrWarnedAsync(provider, mode, () => manager.UpdateAsync("agent-1", new UpdateResourceRequest
        {
            BaseVersion = 2,
            AspectUpdates = new Dictionary<string, object> { ["Links"] = Links("sec-live", "tool-search") },
        }).AsTask(), expectedVersionIfSaved: 3);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.ReferencePinMissing, diagnostic.Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Save_IllTypedReference_ReportsTypeMismatchOnly(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await SeedAsync(provider);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(
            () => CreateAgentAsync(manager, new Dictionary<string, object> { ["Tools"] = new object[] { "missing-tool", 7 } }));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal(1, diagnostic.ElementIndex);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Validator service, bounded reads, configuration
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Validator_PreviewsTheDiagnosticsASaveWouldProduce(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await SeedAsync(provider);

        var diagnostics = await provider.GetRequiredService<IResourceReferenceValidator>().ValidateAsync(
            AgentDefinition(),
            new Dictionary<string, object> { ["Links"] = Links("sec-draft", "tool-search", "nope") });

        Assert.Equal(
            new[] { FacetValueDiagnosticCodes.ReferencePinMissing, FacetValueDiagnosticCodes.ReferenceTargetMissing },
            diagnostics.Select(static diagnostic => diagnostic.Code).ToArray());
    }

    [Fact]
    public async Task Validator_ReadsOnceForTargetsAndOncePerChannel()
    {
        await using var provider = CreateProvider(InMemory);
        await SeedAsync(provider);
        var reader = new CountingVersionReader(provider.GetRequiredService<IResourceVersionReader>());
        var definition = Definition(
            "Agent",
            Aspect(
                "Links",
                Facet("Secret", "reference", new FacetMetadata { ReferenceResolution = Published }),
                Facet("Backup", "reference", new FacetMetadata { ReferenceResolution = Published }),
                Facet("Tools", "reference[]")));

        var diagnostics = await new ResourceReferenceValidator(reader).ValidateAsync(
            definition,
            new Dictionary<string, object>
            {
                ["Links"] = new Dictionary<string, object>
                {
                    ["Secret"] = "sec-live",
                    ["Backup"] = "sec-draft",
                    ["Tools"] = new[] { "tool-search", "note-1", "missing", "tool-search" },
                },
            });

        Assert.Equal(2, reader.Requests.Count);
        Assert.Equal(ResourceVersionScope.Latest, reader.Requests[0].Scope);
        Assert.Equal(new[] { "missing", "note-1", "sec-draft", "sec-live", "tool-search" }, reader.Requests[0].ResourceIds!.Order(StringComparer.Ordinal));
        Assert.Equal(ResourceVersionScope.Active, reader.Requests[1].Scope);
        Assert.Equal(Published, reader.Requests[1].ActivationChannel);
        Assert.Equal(new[] { "sec-draft", "sec-live" }, reader.Requests[1].ResourceIds!.Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { (FacetValueDiagnosticCodes.ReferencePinMissing, "Backup", (int?)null), (FacetValueDiagnosticCodes.ReferenceTargetMissing, "Tools", 2) },
            diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.FacetDefinitionId, diagnostic.ElementIndex)).ToArray());
    }

    [Fact]
    public async Task Validator_WithoutReferenceValues_DoesNotRead()
    {
        var reader = new CountingVersionReader(new InMemoryResourceStore());

        var diagnostics = await new ResourceReferenceValidator(reader).ValidateAsync(
            AgentDefinition(),
            new Dictionary<string, object> { ["Details"] = new Dictionary<string, object> { ["Name"] = "x" } });

        Assert.Empty(diagnostics);
        Assert.Empty(reader.Requests);
    }

    [Fact]
    public void AddAsterCore_DefaultsToStrict()
    {
        using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();

        Assert.Equal(ReferenceValidationMode.Strict, provider.GetRequiredService<AsterOptions>().ReferenceValidation);
    }

    [Theory]
    [InlineData(ReferenceValidationMode.Strict)]
    [InlineData(ReferenceValidationMode.Lenient)]
    public async Task InMemoryResourceManager_HonoursTheMode(ReferenceValidationMode mode)
    {
        var store = new InMemoryResourceStore();
        var definitions = new InMemoryResourceDefinitionStore(NullLogger<InMemoryResourceDefinitionStore>.Instance);
        await definitions.RegisterDefinitionAsync(AgentDefinition());
        var manager = new InMemoryResourceManager(
            store,
            definitions,
            new GuidIdentityGenerator(),
            new AsterOptions { ReferenceValidation = mode },
            NullLogger<InMemoryResourceManager>.Instance);

        var create = () => CreateAgentAsync(manager, Links(tools: ["missing-tool"]));

        if (mode == ReferenceValidationMode.Strict)
        {
            var exception = await Assert.ThrowsAsync<FacetValueValidationException>(create);
            Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetMissing, Assert.Single(exception.Diagnostics).Code);
            Assert.Null(await manager.GetLatestVersionAsync("agent-1"));
        }
        else
        {
            Assert.Equal(1, (await create()).Version);
        }
    }

    public sealed class WarningRecordingHook : ResourceLifecycleHook
    {
        public List<IReadOnlyList<FacetValueDiagnostic>> AfterSaveWarnings { get; } = [];

        public override ValueTask OnAfterSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default)
        {
            if (context.DefinitionId == "Agent")
                AfterSaveWarnings.Add(context.ReferenceWarnings);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingVersionReader(IResourceVersionReader inner) : IResourceVersionReader
    {
        public List<ResourceVersionReadRequest> Requests { get; } = [];

        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(ResourceVersionReadRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.ReadVersionsAsync(request, cancellationToken);
        }
    }
}
