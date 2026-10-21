using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Elsa.Activities;
using Elsa.Common.DistributedHosting;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Helpers;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantScope = Aster.Core.Models.Tenancy.TenantScope;

namespace Aster.Elsa.Tests;

/// <summary>
/// The lifecycle triggers against Elsa 3.9 in process: committed Aster changes start trigger-bound workflows and resume
/// bookmarks whose filters match, once per event; failed writes send nothing; a failing send never fails the write.
/// The hook sends in the background, so each test waits for delivery (<see cref="TriggerTestHost.DeliveredAsync"/>)
/// before it asserts.
/// </summary>
public sealed class AsterTriggerTests
{
    private static readonly string Saved = ActivityTypeNameHelper.GenerateTypeName<ResourceSaved>();
    private static readonly string Activated = ActivityTypeNameHelper.GenerateTypeName<ResourceActivated>();

    [Fact]
    public async Task Activation_starts_each_workflow_whose_filters_match_exactly_once()
    {
        await using var host = await TriggerTestHost.StartAsync();
        var created = await host.CreateProductAsync("gadget");

        await host.Manager.ActivateAsync("gadget", created.Version, "Published");
        await host.DeliveredAsync();

        var published = Assert.Single(host.Runs.Of(OnProductPublished.Id));
        Assert.NotNull(published);
        Assert.Equal(ResourceLifecycleEventKind.Activated, published.Kind);
        Assert.Equal("gadget", published.ResourceId);
        Assert.Equal("Product", published.DefinitionId);
        Assert.Equal(1, published.ResourceVersion);
        Assert.Equal("Published", published.Channel);
        Assert.Equal("", published.TenantId);
        Assert.NotEqual(Guid.Empty, published.OperationId);
        Assert.Single(host.Runs.Of(OnAnyActivation.Id));
        Assert.Empty(host.Runs.Of(OnProductDrafted.Id));
        Assert.Empty(host.Runs.Of(OnOtherDefinitionActivated.Id));
    }

    [Fact]
    public async Task Each_save_starts_the_saved_trigger_once_with_its_version_and_kind()
    {
        await using var host = await TriggerTestHost.StartAsync();

        await host.CreateProductAsync("gadget");
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = new() { ["Price"] = new { Amount = 12 } } });
        await host.DeliveredAsync();

        var runs = host.Runs.Of(OnProductSaved.Id);
        Assert.Equal(2, runs.Count);
        Assert.Equal((ResourceLifecycleEventKind.Saved, 1, ResourceSaveKind.Create), (runs[0]!.Kind, runs[0]!.ResourceVersion, runs[0]!.SaveKind));
        Assert.Equal((ResourceLifecycleEventKind.Saved, 2, ResourceSaveKind.Update), (runs[1]!.Kind, runs[1]!.ResourceVersion, runs[1]!.SaveKind));
    }

    [Fact]
    public async Task Deactivation_and_archive_start_their_triggers_and_a_soft_delete_does_not()
    {
        await using var host = await TriggerTestHost.StartAsync();
        await host.CreateProductAsync("gadget");
        await host.CreateProductAsync("widget");
        await host.Manager.ActivateAsync("gadget", 1, "Published");

        await host.Manager.DeactivateAsync("gadget", 1, "Published");
        var markers = host.Services.GetRequiredService<IResourceLifecycleMarkerService>();
        await markers.ApplyAsync(new ResourceLifecycleMarkerRequest { ResourceId = "gadget", State = ResourceLifecycleMarkerState.Archived, MarkedAt = DateTimeOffset.UtcNow });
        await markers.ApplyAsync(new ResourceLifecycleMarkerRequest { ResourceId = "widget", State = ResourceLifecycleMarkerState.SoftDeleted, MarkedAt = DateTimeOffset.UtcNow });
        await host.DeliveredAsync();

        var unpublished = Assert.Single(host.Runs.Of(OnProductUnpublished.Id))!;
        Assert.Equal((ResourceLifecycleEventKind.Deactivated, "gadget", 1, "Published"), (unpublished.Kind, unpublished.ResourceId, unpublished.ResourceVersion, unpublished.Channel));
        var archived = Assert.Single(host.Runs.Of(OnProductArchived.Id))!;
        Assert.Equal((ResourceLifecycleEventKind.Archived, "gadget", "Product", (int?)null), (archived.Kind, archived.ResourceId, archived.DefinitionId, archived.ResourceVersion));
    }

    [Fact]
    public async Task The_Archive_Resource_activity_starts_the_archived_trigger()
    {
        await using var host = await TriggerTestHost.StartAsync();
        await host.CreateProductAsync("gadget");

        using (var scope = host.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>()
                .RunAsync(new ArchiveResource { ResourceId = new("gadget") });
            result.AssertCompleted();
        }

        await host.DeliveredAsync();
        Assert.Equal("gadget", Assert.Single(host.Runs.Of(OnProductArchived.Id))!.ResourceId);
    }

    [Fact]
    public async Task A_waiting_workflow_resumes_once_on_the_next_matching_save()
    {
        await using var host = await TriggerTestHost.StartAsync();
        var started = await host.StartWorkflowAsync(WaitForProductSaved.Id);
        Assert.Equal(WorkflowSubStatus.Suspended, started.SubStatus);
        Assert.Empty(host.Runs.Of(WaitForProductSaved.Id));

        await host.CreateProductAsync("gadget");
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = new() { ["Price"] = new { Amount = 12 } } });
        await host.DeliveredAsync();

        var resumed = Assert.Single(host.Runs.Of(WaitForProductSaved.Id))!;
        Assert.Equal((ResourceLifecycleEventKind.Saved, "gadget", 1), (resumed.Kind, resumed.ResourceId, resumed.ResourceVersion));
        var instance = await host.Services.GetRequiredService<global::Elsa.Workflows.Management.IWorkflowInstanceStore>()
            .FindAsync(new global::Elsa.Workflows.Management.Filters.WorkflowInstanceFilter { Id = started.WorkflowInstanceId });
        Assert.Equal(WorkflowSubStatus.Finished, instance!.SubStatus);
    }

    [Fact]
    public async Task A_resumed_workflow_that_saves_a_matching_resource_neither_waits_on_itself_nor_loses_the_event()
    {
        // Elsa holds a lock per stimulus while it resumes bookmarks. Sent inline, the workflow's own save would wait
        // on that lock until it timed out; a short timeout keeps that failure quick.
        await using var host = await TriggerTestHost.StartAsync(
            services => services.Configure<DistributedLockingOptions>(options => options.LockAcquisitionTimeout = TimeSpan.FromSeconds(2)),
            elsa => elsa.AddWorkflow<WaitThenCreateProduct>());
        var started = await host.StartWorkflowAsync(WaitThenCreateProduct.Id);
        Assert.Equal(WorkflowSubStatus.Suspended, started.SubStatus);

        await host.CreateProductAsync("gadget");
        await host.DeliveredAsync();

        Assert.Equal("gadget", Assert.Single(host.Runs.Of(WaitThenCreateProduct.Id))!.ResourceId);
        Assert.Equal(["gadget", "created-by-workflow"], host.Runs.Of(OnProductSaved.Id).Select(static e => e!.ResourceId));
        Assert.DoesNotContain(host.Logs.Entries, static e => e.Level >= LogLevel.Warning && e.Category == "Aster.Elsa.AsterLifecycleTriggerHook");
    }

    [Fact]
    public async Task A_workflow_started_by_an_event_resumes_on_the_next_matching_event_not_the_same_one()
    {
        await using var host = await TriggerTestHost.StartAsync(configureElsa: elsa =>
        {
            elsa.AddWorkflow<StartThenWaitForAnySave>();
            elsa.AddWorkflow<StartThenWaitForProductSave>();
        });

        // Each instance waits on a bookmark that one of the event's own stimuli matches: the unfiltered one, or the
        // filtered one that started it.
        await host.CreateProductAsync("gadget");
        await host.DeliveredAsync();

        string[] workflows = [StartThenWaitForAnySave.Id, StartThenWaitForProductSave.Id];
        foreach (var workflow in workflows)
        {
            Assert.Equal(1, Assert.Single(host.Runs.Of(workflow + ":first"))!.ResourceVersion);
            Assert.Empty(host.Runs.Of(workflow + ":second"));
        }

        // The next save resumes the waiting instance once, and starts a new one, which waits in turn.
        await host.Manager.UpdateAsync("gadget", new UpdateResourceRequest { BaseVersion = 1, AspectUpdates = new() { ["Price"] = new { Amount = 12 } } });
        await host.DeliveredAsync();

        foreach (var workflow in workflows)
        {
            var first = host.Runs.Of(workflow + ":first");
            Assert.Equal([1, 2], first.Select(static e => e!.ResourceVersion));
            var second = Assert.Single(host.Runs.Of(workflow + ":second"))!;
            Assert.Equal(2, second.ResourceVersion);
            Assert.Equal(first[1]!.OperationId, second.OperationId);
        }
    }

    [Fact]
    public async Task Stimuli_hash_like_the_indexed_triggers_and_bookmarks()
    {
        await using var host = await TriggerTestHost.StartAsync();
        var hasher = host.Services.GetRequiredService<IStimulusHasher>();

        // The trigger of a workflow with both filters set is indexed under the stimulus that carries both.
        var triggers = await host.Services.GetRequiredService<ITriggerStore>()
            .FindManyAsync(new TriggerFilter { WorkflowDefinitionId = OnProductPublished.Id });
        Assert.Equal(
            hasher.Hash(Activated, new ResourceLifecycleStimulus("", "Product", "Published")),
            Assert.Single(triggers).Hash);

        // A bookmark hashes the same way, without the activity instance ID.
        var started = await host.StartWorkflowAsync(WaitForProductSaved.Id);
        Assert.Equal(
            hasher.Hash(Saved, new ResourceLifecycleStimulus("", "Product")),
            Assert.Single(started.Bookmarks).Hash);
    }

    [Fact]
    public async Task The_hook_sends_one_stimulus_per_combination_of_filled_and_empty_filters()
    {
        await using var host = await TriggerTestHost.StartAsync();
        await host.CreateProductAsync("gadget");

        await host.Manager.ActivateAsync("gadget", 1, "Published");
        await host.DeliveredAsync();

        var sends =host.Sends.All.Where(static s => s.ActivityTypeName == Activated).ToList();
        Assert.Equal(
            [
                new ResourceLifecycleStimulus("", "Product", "Published"),
                new ResourceLifecycleStimulus("", "Product"),
                new ResourceLifecycleStimulus("", null, "Published"),
                new ResourceLifecycleStimulus(""),
            ],
            sends.Select(static s => s.Stimulus));
        Assert.All(sends, s => Assert.IsType<ResourceLifecycleEvent>(s.Metadata!.Input![ResourceLifecycleEvent.WorkflowInputKey]));
        Assert.Equal(
            [new ResourceLifecycleStimulus("", "Product"), new ResourceLifecycleStimulus("")],
            host.Sends.All.Where(static s => s.ActivityTypeName == Saved).Select(static s => s.Stimulus));
    }

    [Fact]
    public async Task A_failed_write_sends_nothing()
    {
        FailingActivationWriter? writer = null;
        await using var host = await TriggerTestHost.StartAsync(services =>
            services.AddSingleton<IResourceVersionWriter>(sp => writer = new FailingActivationWriter((IResourceVersionWriter)sp.GetRequiredService<IResourceVersionReader>())));
        await host.CreateProductAsync("gadget");
        writer!.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Manager.ActivateAsync("gadget", 1, "Published").AsTask());
        await host.DeliveredAsync();

        Assert.DoesNotContain(host.Sends.All, static s => s.ActivityTypeName == Activated);
        Assert.Empty(host.Runs.Of(OnProductPublished.Id));
        Assert.Empty(host.Runs.Of(OnAnyActivation.Id));
    }

    [Fact]
    public async Task A_failing_send_is_logged_and_never_fails_the_committed_write()
    {
        await using var host = await TriggerTestHost.StartAsync(services =>
            services.AddScoped<IStimulusSender, ThrowingStimulusSender>());
        await host.CreateProductAsync("gadget");

        await host.Manager.ActivateAsync("gadget", 1, "Published");
        await host.DeliveredAsync();

        Assert.Equal([1],(await host.Manager.GetActiveVersionsAsync("gadget", "Published")).Select(static r => r.Version));
        var warnings = host.Logs.Entries
            .Where(static e => e.Level == LogLevel.Warning && e.Category == "Aster.Elsa.AsterLifecycleTriggerHook" && e.Message.Contains(Activated))
            .ToList();
        Assert.Equal(4, warnings.Count); // One per stimulus; nothing retries.
        Assert.All(warnings, warning =>
        {
            Assert.Contains("'gadget' version 1", warning.Message);
            Assert.Contains("channel 'Published'", warning.Message);
            Assert.Contains("Elsa tenant ''", warning.Message);
            Assert.IsType<InvalidOperationException>(warning.Exception);
        });
    }

    [Fact]
    public async Task Events_are_sent_in_the_Elsa_tenant_of_the_Aster_write_and_match_only_that_tenants_workflows()
    {
        var tenantA = TenantScope.FromTenantId("tenant-a");
        await using var host = await TriggerTestHost.StartAsync(elsaTenantIds: ["", "tenant-a"]);
        await host.CreateProductAsync("gadget", tenantA);
        await host.CreateProductAsync("gadget");

        await host.Manager.ActivateAsync("gadget", 1, "Published", tenantA, allowMultipleActive: false, CancellationToken.None);
        await host.DeliveredAsync();

        var run = Assert.Single(host.Runs.Of(OnProductPublished.Id))!;
        Assert.Equal("tenant-a", run.TenantId);

        await host.Manager.ActivateAsync("gadget", 1, "Published");
        await host.DeliveredAsync();

        Assert.Equal(["tenant-a", ""], host.Runs.Of(OnProductPublished.Id).Select(static e => e!.TenantId));
    }

    [Fact]
    public async Task An_Aster_tenant_without_an_Elsa_tenant_sends_nothing()
    {
        var tenantB = TenantScope.FromTenantId("tenant-b");
        await using var host = await TriggerTestHost.StartAsync();
        await host.Services.GetRequiredService<IResourceDefinitionStore>()
            .RegisterDefinitionAsync(ElsaTestHost.Product() with { TenantScope = tenantB }, tenantB, CancellationToken.None);

        await host.CreateProductAsync("gadget", tenantB);
        await host.DeliveredAsync();

        Assert.Empty(host.Sends.All);
        Assert.Contains(host.Logs.Entries, static e => e.Level == LogLevel.Debug && e.Message.Contains("no tenant 'tenant-b'"));
    }

    private sealed class ThrowingStimulusSender : IStimulusSender
    {
        public Task<SendStimulusResult> SendAsync(string activityTypeName, object stimulus, StimulusMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Elsa is unavailable.");

        public Task<SendStimulusResult> SendAsync(string stimulusHash, StimulusMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Elsa is unavailable.");
    }

    private sealed class FailingActivationWriter(IResourceVersionWriter inner) : IResourceVersionWriter
    {
        public bool Fail { get; set; }

        public ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default) =>
            inner.SaveVersionAsync(resource, cancellationToken);

        public ValueTask<ActivationState> UpdateActivationAsync(string resourceId, string channel, ActivationState state, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("The store is unavailable.") : inner.UpdateActivationAsync(resourceId, channel, state, cancellationToken);
    }
}
