using System.Text.Json;
using System.Text.Json.Serialization;
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
/// #222: <see cref="ReferenceFacetAttribute"/> opts a typed aspect property into a <c>reference</c> or
/// <c>reference[]</c> facet. The derived facet is the hand-declared one: same definition, same JSON Schema,
/// same registration and save-time validation.
/// </summary>
public sealed class ReferenceFacetAttributeTests : IDisposable
{
    private const string Published = "published";

    private readonly List<string> databasePaths = [];

    public void Dispose()
    {
        foreach (var databasePath in databasePaths)
            SqliteJsonTestDatabase.DeleteFiles(databasePath);
    }

    private sealed record Links
    {
        public required string Name { get; init; }

        [ReferenceFacet("Secret", ReferenceResolution = Published)]
        public required string Secret { get; init; }

        [ReferenceFacet("Tool", "Workflow")]
        public string[] Tools { get; init; } = [];

        [ReferenceFacet]
        public string? Anything { get; init; }

        [ReferenceFacet("Note")]
        [JsonPropertyName("notes")]
        public IReadOnlyList<string> Notes { get; init; } = [];

        [ReferenceFacet("Note")]
        public List<string> Drafts { get; init; } = [];

        [ReferenceFacet("Note")]
        public HashSet<string> Pinned { get; init; } = [];
    }

    /// <summary>The definition <see cref="Links"/> derives, declared by hand.</summary>
    private static ResourceDefinition HandDeclared(string definitionId = "Agent") => new ResourceDefinitionBuilder()
        .WithDefinitionId(definitionId)
        .WithAspect(nameof(Links))
        .WithFacet("Name", "string", isRequired: true)
        .WithFacet("Secret", "reference", isRequired: true)
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Secret"], ReferenceResolution = Published })
        .WithFacet("Tools", "reference[]")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Tool", "Workflow"], UniqueItems = true })
        .WithFacet("Anything", "reference")
        .WithFacet("notes", "reference[]")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Note"] })
        .WithFacet("Drafts", "reference[]")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Note"] })
        .WithFacet("Pinned", "reference[]")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Note"] })
        .Build();

    private static ResourceDefinition Typed(string definitionId = "Agent") => new ResourceDefinitionBuilder()
        .WithDefinitionId(definitionId)
        .WithAspect<Links>()
        .WithFacetMetadata("Tools", new FacetMetadata { UniqueItems = true })
        .Build();

    /// <summary>The definition with its generated IDs replaced, so two builds compare by content.</summary>
    private static string Normalized(ResourceDefinition definition) => JsonSerializer.Serialize(definition with
    {
        Id = "definition",
        AspectDefinitions = definition.AspectDefinitions.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value with
            {
                Id = "aspect",
                FacetDefinitions = pair.Value.FacetDefinitions.Select(static facet => facet with { Id = "facet" }).ToList(),
            }),
    });

    // ──────────────────────────────────────────────────────────────────────────
    // Derivation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Derivation_EqualsTheHandDeclaredDefinition()
    {
        var typed = Typed();

        Assert.Equal(
            [
                ("Name", "string"),
                ("Secret", "reference"),
                ("Tools", "reference[]"),
                ("Anything", "reference"),
                ("notes", "reference[]"),
                ("Drafts", "reference[]"),
                ("Pinned", "reference[]"),
            ],
            typed.AspectDefinitions[nameof(Links)].FacetDefinitions.Select(static facet => (facet.FacetDefinitionId, facet.Type)));
        Assert.Equal(Normalized(HandDeclared()), Normalized(typed));
    }

    [Fact]
    public void Derivation_WithoutOptions_DeclaresNoMetadata()
    {
        var anything = Typed().AspectDefinitions[nameof(Links)].FacetDefinitions.Single(static facet => facet.FacetDefinitionId == "Anything");

        Assert.Null(anything.Metadata);
    }

    [Fact]
    public void SharedAspectFromType_DerivesTheSameReferenceFacets()
    {
        var shared = AspectDefinitionBuilder.FromType<Links>();
        var local = new ResourceDefinitionBuilder().WithDefinitionId("Agent").WithAspect<Links>().Build().AspectDefinitions[nameof(Links)];

        Assert.Equal(
            JsonSerializer.Serialize(local.FacetDefinitions.Select(static facet => facet with { Id = "facet" })),
            JsonSerializer.Serialize(shared.FacetDefinitions.Select(static facet => facet with { Id = "facet" })));
    }

    [Fact]
    public void WithFacetMetadata_KeepsAttributeTargetsUnlessRedeclared()
    {
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Agent")
            .WithAspect<Links>()
            .WithFacetMetadata("Secret", new FacetMetadata { Label = "Secret" })
            .WithFacetMetadata("Tools", new FacetMetadata { TargetDefinitionIds = ["Tool"], ReferenceResolution = "live" })
            .Build();

        var facets = definition.AspectDefinitions[nameof(Links)].FacetDefinitions.ToDictionary(static facet => facet.FacetDefinitionId);
        Assert.Equal("Secret", facets["Secret"].Metadata!.Label);
        Assert.Equal(["Secret"], facets["Secret"].Metadata!.TargetDefinitionIds!);
        Assert.Equal(Published, facets["Secret"].Metadata!.ReferenceResolution);
        Assert.Equal(["Tool"], facets["Tools"].Metadata!.TargetDefinitionIds!);
        Assert.Equal("live", facets["Tools"].Metadata!.ReferenceResolution);
    }

    private sealed class IntReference
    {
        [ReferenceFacet("Prompt")]
        public int Prompt { get; init; }
    }

    private sealed class IntsReference
    {
        [ReferenceFacet("Prompt")]
        public List<int> Prompts { get; init; } = [];
    }

    private sealed class GuidReference
    {
        [ReferenceFacet]
        public Guid Prompt { get; init; }
    }

    private sealed record Nested(string Value);

    private sealed class ObjectReference
    {
        [ReferenceFacet]
        public Nested? Prompt { get; init; }
    }

    private sealed class ConvertedReference
    {
        [ReferenceFacet]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public string? Prompt { get; init; }
    }

    public static TheoryData<Type> NonStringShapes => new()
    {
        typeof(IntReference),
        typeof(IntsReference),
        typeof(GuidReference),
        typeof(ObjectReference),
        typeof(ConvertedReference),
    };

    [Theory]
    [MemberData(nameof(NonStringShapes))]
    public void Derivation_NonStringProperty_ThrowsNamingTheProperty(Type aspectType)
    {
        var withAspect = typeof(ReferenceFacetAttributeTests).GetMethod(nameof(WithAspectOf), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(aspectType);
        var fromType = typeof(AspectDefinitionBuilder).GetMethod(nameof(AspectDefinitionBuilder.FromType))!.MakeGenericMethod(aspectType);

        foreach (var derive in new Action[] { () => withAspect.Invoke(null, []), () => fromType.Invoke(null, [false]) })
        {
            var exception = Assert.IsType<InvalidOperationException>(Assert.Throws<System.Reflection.TargetInvocationException>(derive).InnerException);
            Assert.Contains($"{aspectType.Name}.Prompt", exception.Message);
            Assert.Contains("[ReferenceFacet]", exception.Message);
        }
    }

    private static ResourceDefinitionBuilder WithAspectOf<T>() => new ResourceDefinitionBuilder().WithDefinitionId("Agent").WithAspect<T>();

    [Fact]
    public void WithNamedAspect_NonStringProperty_Throws()
    {
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Agent");

        Assert.Throws<InvalidOperationException>(() => builder.WithNamedAspect<IntReference>("Primary"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // JSON Schema export
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaExport_EqualsTheHandDeclaredSchema()
    {
        var exporter = new ServiceCollection().AddAsterCore().BuildServiceProvider().GetRequiredService<IResourceSchemaExporter>();

        var typed = exporter.Export(Typed());

        Assert.Equal(exporter.Export(HandDeclared()).ToJsonString(), typed.ToJsonString());
        Assert.Contains("x-aster-reference", typed.ToJsonString());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Registration and save-time validation
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class BlankTarget
    {
        [ReferenceFacet("Tool", " ", "Tool")]
        public string[] Tools { get; init; } = [];

        [ReferenceFacet(ReferenceResolution = " ")]
        public string? Secret { get; init; }
    }

    [Fact]
    public async Task Registration_InvalidOptions_FailsLikeHandDeclaredMetadata()
    {
        await using var provider = new ServiceCollection().AddAsterCore().BuildServiceProvider();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();

        var typed = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder().WithDefinitionId("Typed").WithAspect<BlankTarget>().Build()));
        var hand = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId("Hand")
                .WithAspect(nameof(BlankTarget))
                .WithFacet("Tools", "reference[]")
                .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Tool", " ", "Tool"] })
                .WithFacet("Secret", "reference")
                .WithFacetMetadata(new FacetMetadata { ReferenceResolution = " " })
                .Build()));

        Assert.Equal(
            [
                (FacetMetadataDiagnosticCodes.ReferenceResolutionInvalid, "Secret"),
                (FacetMetadataDiagnosticCodes.ReferenceTargetsInvalid, "Tools"),
            ],
            typed.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.FacetDefinitionId)));
        Assert.Equal(
            hand.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)),
            typed.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)));
    }

    private ServiceProvider CreateProvider(bool sqlite)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (sqlite)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");
            databasePaths.Add(databasePath);
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Registers targets and the agent definition, and seeds one live secret, one tool and one note.</summary>
    private static async Task<IResourceManager> SeedAsync(ServiceProvider provider, ResourceDefinition agentDefinition)
    {
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        foreach (var definitionId in new[] { "Secret", "Tool", "Note" })
        {
            await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId(definitionId)
                .WithAspect("Details")
                .WithFacet("Name", "string")
                .Build());
        }

        await definitions.RegisterDefinitionAsync(agentDefinition);

        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.CreateAsync("Secret", new CreateResourceRequest { ResourceId = "sec-live" });
        await manager.CreateAsync("Secret", new CreateResourceRequest { ResourceId = "sec-draft" });
        await manager.CreateAsync("Tool", new CreateResourceRequest { ResourceId = "tool-search" });
        await manager.CreateAsync("Note", new CreateResourceRequest { ResourceId = "note-1" });
        await manager.ActivateAsync("sec-live", 1, Published);
        return manager;
    }

    public static TheoryData<bool, string, string, string[], string> Violations => new()
    {
        // provider (sqlite?), case, secret, tools, expected code
        { false, "dangling", "sec-gone", ["tool-search"], FacetValueDiagnosticCodes.ReferenceTargetMissing },
        { false, "wrong target", "sec-live", ["tool-search", "note-1"], FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch },
        { false, "unpinned", "sec-draft", [], FacetValueDiagnosticCodes.ReferencePinMissing },
        { false, "duplicate", "sec-live", ["tool-search", "tool-search"], FacetValueDiagnosticCodes.ItemsNotUnique },
        { true, "dangling", "sec-gone", ["tool-search"], FacetValueDiagnosticCodes.ReferenceTargetMissing },
        { true, "wrong target", "sec-live", ["tool-search", "note-1"], FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch },
        { true, "unpinned", "sec-draft", [], FacetValueDiagnosticCodes.ReferencePinMissing },
        { true, "duplicate", "sec-live", ["tool-search", "tool-search"], FacetValueDiagnosticCodes.ItemsNotUnique },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public async Task Save_InvalidReference_IsRejectedLikeTheHandDeclaredFacet(bool sqlite, string @case, string secret, string[] tools, string expectedCode)
    {
        _ = @case;
        var diagnostics = new List<IReadOnlyList<FacetValueDiagnostic>>();
        foreach (var definition in new[] { HandDeclared(), Typed() })
        {
            await using var provider = CreateProvider(sqlite);
            var manager = await SeedAsync(provider, definition);

            var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
                await manager.CreateAsync("Agent", new CreateResourceRequest
                {
                    ResourceId = "agent-1",
                    InitialAspects = new Dictionary<string, object> { [nameof(Links)] = new Links { Name = "Researcher", Secret = secret, Tools = tools } },
                }));
            Assert.Null(await manager.GetLatestVersionAsync("agent-1"));
            diagnostics.Add(exception.Diagnostics);
        }

        Assert.Equal(expectedCode, Assert.Single(diagnostics[1]).Code);
        Assert.Equal(diagnostics[0], diagnostics[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ValidReferences_SavesTheTypedAspect(bool sqlite)
    {
        await using var provider = CreateProvider(sqlite);
        var manager = await SeedAsync(provider, Typed());

        var saved = await manager.CreateAsync("Agent", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object>
            {
                [nameof(Links)] = new Links
                {
                    Name = "Researcher",
                    Secret = "sec-live",
                    Tools = ["tool-search"],
                    Anything = "note-1",
                    Notes = ["note-1"],
                    Drafts = ["note-1"],
                    Pinned = ["note-1"],
                },
            },
        });

        Assert.Equal(1, saved.Version);
        var stored = (await manager.GetLatestVersionAsync(saved.ResourceId))!
            .GetAspect<Links>(nameof(Links), provider.GetRequiredService<ITypedAspectBinder>())!;
        Assert.Equal("sec-live", stored.Secret);
        Assert.Equal(new[] { "tool-search" }, stored.Tools);
    }
}
