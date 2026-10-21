using System.Reflection;
using Aster.Core.Models.Instances;
using Aster.Elsa.Activities;
using CShells.Features;
using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aster.Elsa.Tests;

/// <summary>
/// Both Elsa feature systems register the same activities, and Elsa describes them for its designer.
/// </summary>
public sealed class AsterFeatureTests
{
    private static readonly Type[] Activities =
    [
        typeof(CreateResource),
        typeof(UpdateResource),
        typeof(GetResource),
        typeof(QueryResources),
        typeof(ActivateResourceVersion),
        typeof(DeactivateResourceVersion),
        typeof(PromoteResource),
        typeof(ArchiveResource),
        typeof(ResourceSaved),
        typeof(ResourceActivated),
        typeof(ResourceDeactivated),
        typeof(ResourceArchived),
    ];

    [Fact]
    public async Task UseAster_registers_every_activity_with_Elsa()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var registered = host.Services.GetRequiredService<IOptions<ManagementOptions>>().Value.ActivityTypes;

        Assert.All(Activities, activity => Assert.Contains(activity, registered));
    }

    [Fact]
    public void The_shell_feature_registers_the_same_activities()
    {
        var services = new ServiceCollection();
        new ShellFeatures.AsterFeature().ConfigureServices(services);

        var registered = services.BuildServiceProvider().GetRequiredService<IOptions<ManagementOptions>>().Value.ActivityTypes;

        Assert.Equal(Activities.OrderBy(static t => t.Name), registered.OrderBy(static t => t.Name));
    }

    [Fact]
    public void The_shell_feature_is_named_and_depends_on_Elsa_workflow_management_and_runtime()
    {
        var attribute = typeof(ShellFeatures.AsterFeature).GetCustomAttribute<ShellFeatureAttribute>()!;

        Assert.Equal("Aster", attribute.Name);
        Assert.Contains(typeof(global::Elsa.Workflows.Management.ShellFeatures.WorkflowManagementFeature), attribute.DependsOn!);
        Assert.Contains(typeof(global::Elsa.Workflows.Runtime.ShellFeatures.WorkflowRuntimeFeature), attribute.DependsOn!);
    }

    [Fact]
    public async Task Elsa_describes_the_activities_for_its_designer()
    {
        await using var host = await ElsaTestHost.StartAsync();
        var describer = host.Services.GetRequiredService<IActivityDescriber>();

        var create = await describer.DescribeActivityAsync(typeof(CreateResource));

        Assert.Equal("Aster.CreateResource", create.TypeName);
        Assert.Equal("Aster", create.Category);
        Assert.Equal("Create Resource", create.DisplayName);
        Assert.Equal(["Aspects", "DefinitionId", "ResourceId"], create.Inputs.Select(static i => i.Name).Order());
        Assert.Equal(InputUIHints.JsonEditor, create.Inputs.Single(static i => i.Name == "Aspects").UIHint);
        Assert.Equal("Result", Assert.Single(create.Outputs).Name);

        foreach (var activity in Activities)
            Assert.StartsWith("Aster.", (await describer.DescribeActivityAsync(activity)).TypeName);
    }

    [Fact]
    public async Task A_serialized_workflow_with_literal_inputs_round_trips_and_runs()
    {
        await using var host = await ElsaTestHost.StartAsync();
        await host.Services.GetRequiredService<IActivityRegistryPopulator>().PopulateRegistryAsync();
        var serializer = host.Services.GetRequiredService<IActivitySerializer>();

        // What a designer saves: literal inputs, the aspects as JSON text.
        var json = serializer.Serialize(new CreateResource
        {
            DefinitionId = new("Product"),
            ResourceId = new("gadget"),
            Aspects = new("""{ "Title": { "Title": "Gadget" } }"""),
        });
        Assert.Contains("\"Aster.CreateResource\"", json);

        var result = await host.RunAsync(serializer.Deserialize(json));

        result.AssertCompleted();
        Assert.NotNull(await host.Manager.GetLatestVersionAsync("gadget"));
    }

    [Fact]
    public async Task Activity_results_feed_later_activities_through_expressions()
    {
        await using var host = await ElsaTestHost.StartAsync();
        var created = new Variable<Resource>();
        var updated = new Variable<Resource>();
        var captured = new List<object?>();

        // Create, then update the created resource on its own version, then publish the update.
        var result = await host.RunAsync(
            new CreateResource { DefinitionId = new("Product"), Aspects = new("""{ "Title": { "Title": "Gadget" } }"""), Result = new(created) },
            new UpdateResource
            {
                ResourceId = new(context => created.Get(context)!.ResourceId),
                BaseVersion = new(context => created.Get(context)!.Version),
                Aspects = new(context => (object?)new { Title = new { Title = $"Gadget v{created.Get(context)!.Version + 1}" } }),
                Result = new(updated),
            },
            new ActivateResourceVersion
            {
                ResourceId = new(context => updated.Get(context)!.ResourceId),
                ResourceVersion = new(context => updated.Get(context)!.Version),
                Channel = new("Published"),
            },
            new Capture(captured, new(updated)));

        result.AssertCompleted();
        var resource = Assert.IsType<Resource>(Assert.Single(captured));
        Assert.Equal(2, resource.Version);
        Assert.Equal([2], (await host.Manager.GetActiveVersionsAsync(resource.ResourceId, "Published")).Select(static r => r.Version));
    }
}
