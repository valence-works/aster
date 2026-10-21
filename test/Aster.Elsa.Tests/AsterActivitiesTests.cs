using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Elsa.Activities;
using Elsa.Workflows.Memory;
using Microsoft.Extensions.DependencyInjection;
using TenantScope = Aster.Core.Models.Tenancy.TenantScope;

namespace Aster.Elsa.Tests;

/// <summary>
/// Runs real Elsa workflows over the Aster activities, on the in-memory provider and on SQLite.
/// </summary>
public sealed class AsterActivitiesTests
{
    public static TheoryData<string> Providers => new() { ElsaTestHost.InMemory, ElsaTestHost.Sqlite };

    private const string Gadget = """{ "Title": { "Title": "Gadget" }, "Price": { "Amount": 10 } }""";

    // Create Resource

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Create_saves_version_1_and_returns_it(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        var captured = new List<object?>();
        var created = new Variable<Resource>();

        var result = await host.RunAsync(
            new CreateResource { DefinitionId = new("Product"), ResourceId = new("gadget"), Aspects = new(Gadget), Result = new(created) },
            new Capture(captured, new(created)));

        result.AssertCompleted();
        var resource = Assert.IsType<Resource>(Assert.Single(captured));
        Assert.Equal("gadget", resource.ResourceId);
        Assert.Equal(1, resource.Version);
        Assert.Equal(TenantScope.Default, resource.TenantScope);

        var stored = await host.Manager.GetLatestVersionAsync("gadget");
        Assert.NotNull(stored);
        Assert.Equal("Gadget", Title(stored));
    }

    public static TheoryData<object> AspectShapes()
    {
        dynamic expando = new ExpandoObject();
        expando.Title = new Dictionary<string, object?> { ["Title"] = "Gadget" };
        return new TheoryData<object>
        {
            Gadget,
            JsonDocument.Parse(Gadget).RootElement,
            JsonNode.Parse(Gadget)!.AsObject(),
            new Dictionary<string, object?> { ["Title"] = new Dictionary<string, object?> { ["Title"] = "Gadget" } },
            (ExpandoObject)expando,
            new { Title = new { Title = "Gadget" } },
        };
    }

    [Theory]
    [MemberData(nameof(AspectShapes))]
    public async Task Create_accepts_aspects_as_json_text_json_values_dictionaries_and_objects(object aspects)
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new CreateResource { DefinitionId = new("Product"), ResourceId = new("gadget"), Aspects = new(aspects) });

        result.AssertCompleted();
        Assert.Equal("Gadget", Title((await host.Manager.GetLatestVersionAsync("gadget"))!));
    }

    [Fact]
    public async Task Create_generates_a_resource_id_when_none_is_given()
    {
        await using var host = await ElsaTestHost.StartAsync();
        var captured = new List<object?>();
        var created = new Variable<Resource>();

        var result = await host.RunAsync(
            new CreateResource { DefinitionId = new("Product"), Aspects = new(Gadget), Result = new(created) },
            new Capture(captured, new(created)));

        result.AssertCompleted();
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<Resource>(Assert.Single(captured)).ResourceId));
    }

    [Fact]
    public async Task Create_faults_with_definition_not_found_for_an_unknown_definition()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new CreateResource { DefinitionId = new("Missing"), Aspects = new(Gadget) });

        Assert.Equal(AsterActivityFaultCodes.DefinitionNotFound, result.FaultCode());
    }

    [Fact]
    public async Task Create_faults_with_definition_id_required_for_an_empty_definition()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new CreateResource { DefinitionId = new(" "), Aspects = new(Gadget) });

        Assert.Equal(AsterActivityFaultCodes.DefinitionIdRequired, result.FaultCode());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Create_faults_with_every_facet_value_diagnostic_and_writes_nothing(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);

        var result = await host.RunAsync(new CreateResource
        {
            DefinitionId = new("Product"),
            ResourceId = new("gadget"),
            Aspects = new("""{ "Title": { "Title": "A title that is far longer than twenty characters" } }"""),
        });

        Assert.Equal("facet-value-length-out-of-range", result.FaultCode());
        var diagnostic = Assert.Single(result.FaultDiagnostics().EnumerateArray());
        Assert.Equal("facet-value-length-out-of-range", diagnostic.GetProperty("code").GetString());
        Assert.Equal("Title", diagnostic.GetProperty("aspectKey").GetString());
        Assert.Equal("Title", diagnostic.GetProperty("facetId").GetString());
        Assert.Null(await host.Manager.GetLatestVersionAsync("gadget"));
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    [InlineData("{ not json")]
    public async Task Create_faults_with_aspects_invalid_for_anything_but_a_json_object(string aspects)
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new CreateResource { DefinitionId = new("Product"), Aspects = new(aspects) });

        Assert.Equal(AsterActivityFaultCodes.AspectsInvalid, result.FaultCode());
    }

    [Fact]
    public async Task Create_faults_with_duplicate_resource_id_for_an_existing_id()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await CreateGadgetAsync(host);

        var result = await host.RunAsync(new CreateResource { DefinitionId = new("Product"), ResourceId = new("gadget"), Aspects = new(Gadget) });

        Assert.Equal(AsterActivityFaultCodes.DuplicateResourceId, result.FaultCode());
    }

    [Fact]
    public async Task Identifiers_that_start_or_end_with_whitespace_fault_with_identifier_whitespace_and_write_nothing()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await CreateGadgetAsync(host);

        Assert.Equal(
            InvalidIdentifierException.SurroundingWhitespaceCode,
            (await host.RunAsync(new CreateResource { DefinitionId = new("Product"), ResourceId = new(" padded"), Aspects = new(Gadget) })).FaultCode());
        Assert.Equal(
            InvalidIdentifierException.SurroundingWhitespaceCode,
            (await host.RunAsync(new ActivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(1), Channel = new("Published ") })).FaultCode());
        Assert.Null(await host.Manager.GetLatestVersionAsync(" padded"));
        Assert.Empty(await host.Manager.GetActiveVersionsAsync("gadget", "Published "));
    }

    // Update Resource

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Update_saves_a_new_version_on_the_base_version(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host);
        var captured = new List<object?>();
        var updated = new Variable<Resource>();

        var result = await host.RunAsync(
            new UpdateResource { ResourceId = new("gadget"), BaseVersion = new(1), Aspects = new("""{ "Title": { "Title": "Gadget Pro" } }"""), Result = new(updated) },
            new Capture(captured, new(updated)));

        result.AssertCompleted();
        var resource = Assert.IsType<Resource>(Assert.Single(captured));
        Assert.Equal(2, resource.Version);
        Assert.Equal("Gadget Pro", Title(resource));
        Assert.True(resource.Aspects.ContainsKey("Price"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Update_on_a_stale_base_version_faults_with_concurrency_conflict_and_writes_nothing(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host);
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = Aspects("""{ "Title": { "Title": "Someone else" } }""") });

        var result = await host.RunAsync(new UpdateResource { ResourceId = new("gadget"), BaseVersion = new(1), Aspects = new("""{ "Title": { "Title": "Mine" } }""") });

        Assert.Equal(AsterActivityFaultCodes.ConcurrencyConflict, result.FaultCode());
        Assert.Contains("expected version 1", Assert.Single(result.WorkflowState.Incidents).Message);
        var latest = await host.Manager.GetLatestVersionAsync("gadget");
        Assert.Equal(2, latest!.Version);
        Assert.Equal("Someone else", Title(latest));
    }

    [Fact]
    public async Task Update_faults_with_resource_not_found_for_a_missing_resource()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new UpdateResource { ResourceId = new("missing"), BaseVersion = new(1), Aspects = new(Gadget) });

        Assert.Equal(AsterActivityFaultCodes.ResourceNotFound, result.FaultCode());
    }

    // Get Resource

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Get_reads_the_latest_a_specific_or_the_active_version(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host);
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = Aspects("""{ "Title": { "Title": "Gadget Pro" } }""") });
        await host.Manager.ActivateAsync("gadget", 1, "Published");
        var captured = new List<object?>();
        var latest = new Variable<Resource?>();
        var first = new Variable<Resource?>();
        var published = new Variable<Resource?>();
        var draft = new Variable<Resource?>();

        var result = await host.RunAsync(
            new GetResource { ResourceId = new("gadget"), Result = new(latest) },
            new GetResource { ResourceId = new("gadget"), ResourceVersion = new(1), Result = new(first) },
            new GetResource { ResourceId = new("gadget"), Channel = new("Published"), Result = new(published) },
            new GetResource { ResourceId = new("gadget"), Channel = new("Staging"), Result = new(draft) },
            new Capture(captured, new(latest)),
            new Capture(captured, new(first)),
            new Capture(captured, new(published)),
            new Capture(captured, new(draft)));

        result.AssertCompleted();
        Assert.Equal(2, Assert.IsType<Resource>(captured[0]).Version);
        Assert.Equal(1, Assert.IsType<Resource>(captured[1]).Version);
        Assert.Equal(1, Assert.IsType<Resource>(captured[2]).Version);
        Assert.Null(captured[3]);
    }

    [Fact]
    public async Task Get_returns_nothing_for_a_missing_resource()
    {
        await using var host = await ElsaTestHost.StartAsync();
        var captured = new List<object?>();
        var missing = new Variable<Resource?>();

        var result = await host.RunAsync(
            new GetResource { ResourceId = new("missing"), Result = new(missing) },
            new Capture(captured, new(missing)));

        result.AssertCompleted();
        Assert.Null(Assert.Single(captured));
    }

    [Fact]
    public async Task Get_faults_with_resource_selector_invalid_for_both_a_version_and_a_channel()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await CreateGadgetAsync(host);

        var result = await host.RunAsync(new GetResource { ResourceId = new("gadget"), ResourceVersion = new(1), Channel = new("Published") });

        Assert.Equal(AsterActivityFaultCodes.ResourceSelectorInvalid, result.FaultCode());
    }

    // Query Resources

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Query_hides_archived_and_soft_deleted_resources_unless_asked(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host, "live");
        await CreateGadgetAsync(host, "archived");
        await CreateGadgetAsync(host, "deleted");
        var markers = host.Services.GetRequiredService<IResourceLifecycleMarkerService>();
        await Mark(markers, "archived", ResourceLifecycleMarkerState.Archived);
        await Mark(markers, "deleted", ResourceLifecycleMarkerState.SoftDeleted);
        var captured = new List<object?>();
        var visible = new Variable<IReadOnlyList<Resource>>();
        var all = new Variable<IReadOnlyList<Resource>>();
        var archivedOnly = new Variable<IReadOnlyList<Resource>>();

        var result = await host.RunAsync(
            new QueryResources { DefinitionId = new("Product"), Result = new(visible) },
            new QueryResources { DefinitionId = new("Product"), IncludeArchived = new(true), Result = new(all) },
            new QueryResources
            {
                Query = new(new ResourceQuery { LifecycleState = ResourceLifecycleMarkerState.Archived }),
                Result = new(archivedOnly),
            },
            new Capture(captured, new(visible)),
            new Capture(captured, new(all)),
            new Capture(captured, new(archivedOnly)));

        result.AssertCompleted();
        Assert.Equal(["live"], Ids(captured[0]));
        Assert.Equal(["archived", "deleted", "live"], Ids(captured[1]));
        Assert.Equal(["archived"], Ids(captured[2]));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Query_runs_the_portable_query_with_filters_scope_and_sorting(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host, "a", "Alpha");
        await CreateGadgetAsync(host, "b", "Beta");
        await CreateGadgetAsync(host, "c", "Gamma");
        await host.Manager.ActivateAsync("a", 1, "Published");
        await host.Manager.ActivateAsync("c", 1, "Published");
        var captured = new List<object?>();
        var rows = new Variable<IReadOnlyList<Resource>>();

        var result = await host.RunAsync(
            new QueryResources
            {
                Query = new(new ResourceQuery
                {
                    Scope = ResourceVersionScope.Active,
                    ActivationChannel = "Published",
                    Filter = new FacetValueFilter("Title", "Title", "a", ComparisonOperator.Contains),
                    Sorts = [new SortExpression("Title", SortDirection.Descending, "Title")],
                }),
                Result = new(rows),
            },
            new Capture(captured, new(rows)));

        result.AssertCompleted();
        Assert.Equal(["c", "a"], Ids(Assert.Single(captured), sort: false));
    }

    [Fact]
    public async Task Query_faults_with_every_validation_failure()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new QueryResources { Query = new(new ResourceQuery { Skip = -1, Take = -1 }) });

        Assert.Equal("negative-skip", result.FaultCode());
        var codes = result.FaultDiagnostics().EnumerateArray().Select(static d => d.GetProperty("code").GetString()).ToList();
        Assert.Equal(["negative-skip", "negative-take"], codes);
        Assert.Equal("paging", result.FaultDiagnostics()[0].GetProperty("feature").GetString());
    }

    [Fact]
    public async Task Query_faults_with_tenant_scope_mismatch_for_a_query_in_another_tenant()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(new QueryResources { Query = new(new ResourceQuery { TenantScope = TenantScope.FromTenantId("other") }) });

        Assert.Equal("tenant-scope-mismatch", result.FaultCode());
    }

    // Activate, deactivate and promote

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Activate_and_deactivate_change_the_versions_active_in_a_channel(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host);
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = Aspects("""{ "Title": { "Title": "Gadget Pro" } }""") });
        var captured = new List<object?>();
        var activated = new Variable<Resource>();

        var result = await host.RunAsync(
            new ActivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(1), Channel = new("Published") },
            new ActivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(2), Channel = new("Published"), AllowMultipleActive = new(true), Result = new(activated) },
            new Capture(captured, new(activated)));

        result.AssertCompleted();
        Assert.Equal(2, Assert.IsType<Resource>(Assert.Single(captured)).Version);
        Assert.Equal([1, 2], await ActiveVersionsAsync(host, "Published"));

        result = await host.RunAsync(new DeactivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(1), Channel = new("Published") });

        result.AssertCompleted();
        Assert.Equal([2], await ActiveVersionsAsync(host, "Published"));

        // Without AllowMultipleActive, activation replaces what is active: a rollback to version 1.
        result = await host.RunAsync(new ActivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(1), Channel = new("Published") });

        result.AssertCompleted();
        Assert.Equal([1], await ActiveVersionsAsync(host, "Published"));
    }

    [Fact]
    public async Task Activate_faults_with_stable_codes_for_a_missing_resource_version_or_channel()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await CreateGadgetAsync(host);

        Assert.Equal(
            AsterActivityFaultCodes.ResourceNotFound,
            (await host.RunAsync(new ActivateResourceVersion { ResourceId = new("missing"), ResourceVersion = new(1), Channel = new("Published") })).FaultCode());
        Assert.Equal(
            AsterActivityFaultCodes.VersionNotFound,
            (await host.RunAsync(new ActivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(9), Channel = new("Published") })).FaultCode());
        Assert.Equal(
            AsterActivityFaultCodes.ChannelRequired,
            (await host.RunAsync(new ActivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(1), Channel = new("") })).FaultCode());
        Assert.Equal(
            AsterActivityFaultCodes.VersionNotFound,
            (await host.RunAsync(new DeactivateResourceVersion { ResourceId = new("gadget"), ResourceVersion = new(9), Channel = new("Published") })).FaultCode());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Promote_activates_the_version_active_in_the_source_channel_in_the_target_channel(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host);
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = Aspects("""{ "Title": { "Title": "Gadget Pro" } }""") });
        await host.Manager.ActivateAsync("gadget", 1, "production");
        await host.Manager.ActivateAsync("gadget", 2, "staging");
        var captured = new List<object?>();
        var promoted = new Variable<Resource>();

        var result = await host.RunAsync(
            new PromoteResource { ResourceId = new("gadget"), SourceChannel = new("staging"), TargetChannel = new("production"), Result = new(promoted) },
            new Capture(captured, new(promoted)));

        result.AssertCompleted();
        Assert.Equal(2, Assert.IsType<Resource>(Assert.Single(captured)).Version);
        Assert.Equal([2], await ActiveVersionsAsync(host, "production"));
        Assert.Equal([2], await ActiveVersionsAsync(host, "staging"));
    }

    [Fact]
    public async Task Promote_faults_when_the_source_channel_has_no_or_several_active_versions()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await CreateGadgetAsync(host);
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = Aspects("""{ "Title": { "Title": "Gadget Pro" } }""") });

        var result = await host.RunAsync(new PromoteResource { ResourceId = new("gadget"), SourceChannel = new("staging"), TargetChannel = new("production") });
        Assert.Equal(AsterActivityFaultCodes.PromoteSourceNotActive, result.FaultCode());

        await host.Manager.ActivateAsync("gadget", 1, "staging");
        await host.Manager.ActivateAsync("gadget", 2, "staging", allowMultipleActive: true);
        result = await host.RunAsync(new PromoteResource { ResourceId = new("gadget"), SourceChannel = new("staging"), TargetChannel = new("production") });
        Assert.Equal(AsterActivityFaultCodes.PromoteSourceAmbiguous, result.FaultCode());
        Assert.Empty(await ActiveVersionsAsync(host, "production"));

        result = await host.RunAsync(new PromoteResource { ResourceId = new("missing"), SourceChannel = new("staging"), TargetChannel = new("production") });
        Assert.Equal(AsterActivityFaultCodes.ResourceNotFound, result.FaultCode());
    }

    // Archive Resource

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Archive_marks_the_resource_and_queries_then_hide_it(string provider)
    {
        await using var host = await ElsaTestHost.StartAsync(provider);
        await CreateGadgetAsync(host);
        var captured = new List<object?>();
        var marker = new Variable<ResourceLifecycleMarker>();
        var visible = new Variable<IReadOnlyList<Resource>>();

        var result = await host.RunAsync(
            new ArchiveResource { ResourceId = new("gadget"), Reason = new("Discontinued"), Result = new(marker) },
            new QueryResources { DefinitionId = new("Product"), Result = new(visible) },
            new Capture(captured, new(marker)),
            new Capture(captured, new(visible)));

        result.AssertCompleted();
        var archived = Assert.IsType<ResourceLifecycleMarker>(captured[0]);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, archived.State);
        Assert.Equal("Discontinued", archived.Reason);
        Assert.Empty(Ids(captured[1]));
        Assert.Equal(ResourceLifecycleMarkerState.Archived, (await host.Services.GetRequiredService<IResourceLifecycleMarkerService>().GetAsync("gadget"))!.State);
        Assert.NotNull(await host.Manager.GetLatestVersionAsync("gadget"));
    }

    [Fact]
    public async Task Archive_faults_with_resource_not_found_or_aster_lifecycle_marker_codes()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await CreateGadgetAsync(host);
        await Mark(host.Services.GetRequiredService<IResourceLifecycleMarkerService>(), "gadget", ResourceLifecycleMarkerState.SoftDeleted);

        Assert.Equal(AsterActivityFaultCodes.ResourceNotFound, (await host.RunAsync(new ArchiveResource { ResourceId = new("missing") })).FaultCode());
        Assert.Equal("lifecycle-marker-conflict", (await host.RunAsync(new ArchiveResource { ResourceId = new("gadget") })).FaultCode());
    }

    // Helpers

    private static async Task CreateGadgetAsync(ElsaTestHost host, string resourceId = "gadget", string title = "Gadget") =>
        await host.Manager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = resourceId,
            InitialAspects = Aspects($$"""{ "Title": { "Title": "{{title}}" }, "Price": { "Amount": 10 } }"""),
        });

    private static Dictionary<string, object> Aspects(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(static p => p.Name, static p => (object)p.Value.Clone());

    private static string? Title(Resource resource) => resource.Aspects["Title"] switch
    {
        JsonElement element => element.GetProperty("Title").GetString(),
        var other => JsonSerializer.SerializeToElement(other).GetProperty("Title").GetString(),
    };

    private static List<string> Ids(object? rows, bool sort = true)
    {
        var ids = Assert.IsAssignableFrom<IEnumerable<Resource>>(rows).Select(static r => r.ResourceId).ToList();
        if (sort)
            ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    private static async Task<List<int>> ActiveVersionsAsync(ElsaTestHost host, string channel) =>
        (await host.Manager.GetActiveVersionsAsync("gadget", channel)).Select(static r => r.Version).Order().ToList();

    private static async Task Mark(IResourceLifecycleMarkerService markers, string resourceId, ResourceLifecycleMarkerState state)
    {
        var result = await markers.ApplyAsync(new ResourceLifecycleMarkerRequest { ResourceId = resourceId, State = state, MarkedAt = DateTimeOffset.UtcNow });
        Assert.True(result.Succeeded);
    }
}
