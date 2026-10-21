using System.Collections.Concurrent;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Elsa.Activities;
using Elsa.Common.Features;
using Elsa.Common.Models;
using Elsa.Common.Multitenancy;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantScope = Aster.Core.Models.Tenancy.TenantScope;

namespace Aster.Elsa.Tests;

/// <summary>
/// An in-process Elsa host with the Aster module and the trigger workflows below, populated and indexed in each Elsa
/// tenant, as Elsa does at startup. Every stimulus the hook sends and every workflow run is recorded.
/// </summary>
internal sealed class TriggerTestHost : IAsyncDisposable
{
    private readonly ServiceProvider services;

    private TriggerTestHost(ServiceProvider services) => this.services = services;

    public IServiceProvider Services => services;

    public IResourceManager Manager => services.GetRequiredService<IResourceManager>();

    public TriggerLog Runs => services.GetRequiredService<TriggerLog>();

    public SendLog Sends => services.GetRequiredService<SendLog>();

    public LogSink Logs => services.GetRequiredService<LogSink>();

    /// <summary>
    /// Starts a host whose Elsa tenants are <paramref name="elsaTenantIds"/> (default: Elsa's default tenant only),
    /// with the <c>Product</c> definition registered in the matching Aster tenants. <paramref name="configureElsa"/>
    /// adds a test's own workflows.
    /// </summary>
    public static async Task<TriggerTestHost> StartAsync(
        Action<IServiceCollection>? configure = null,
        Action<IModule>? configureElsa = null,
        params string[] elsaTenantIds)
    {
        var tenantIds = elsaTenantIds.Length == 0 ? [Tenant.DefaultTenantId] : elsaTenantIds;
        var collection = new ServiceCollection();
        var logs = new LogSink();
        collection.AddSingleton(logs);
        collection.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        collection.AddSingleton<TriggerLog>();
        collection.AddSingleton<SendLog>();
        collection.AddAsterCore();
        collection.AddElsa(elsa =>
        {
            elsa.UseAster();
            elsa.AddWorkflow<OnProductPublished>();
            elsa.AddWorkflow<OnAnyActivation>();
            elsa.AddWorkflow<OnProductDrafted>();
            elsa.AddWorkflow<OnOtherDefinitionActivated>();
            elsa.AddWorkflow<OnProductSaved>();
            elsa.AddWorkflow<OnProductUnpublished>();
            elsa.AddWorkflow<OnProductArchived>();
            elsa.AddWorkflow<WaitForProductSaved>();
            configureElsa?.Invoke(elsa);
            if (elsaTenantIds.Length > 0)
                elsa.Configure<MultitenancyFeature>(feature => feature.UseTenantsProvider(_ => new FixedTenantsProvider(tenantIds)));
        });

        // Record every stimulus on its way to Elsa's own sender.
        collection.AddScoped<IStimulusSender>(sp => new RecordingStimulusSender(
            ActivatorUtilities.CreateInstance<StimulusSender>(sp),
            sp.GetRequiredService<SendLog>()));
        configure?.Invoke(collection);

        var host = new TriggerTestHost(collection.BuildServiceProvider());
        var accessor = host.services.GetRequiredService<ITenantAccessor>();
        var definitions = host.services.GetRequiredService<IResourceDefinitionStore>();
        foreach (var tenantId in tenantIds)
        {
            using (accessor.PushContext(new Tenant { Id = tenantId, Name = tenantId }))
            {
                using var scope = host.services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IRegistriesPopulator>().PopulateAsync();
            }

            var asterTenant = AsterTenantMapping.FromElsaTenantId(tenantId);
            await definitions.RegisterDefinitionAsync(ElsaTestHost.Product() with { TenantScope = asterTenant }, asterTenant, CancellationToken.None);
        }

        return host;
    }

    /// <summary>Starts a workflow by definition ID through Elsa's runtime, so its bookmarks are stored.</summary>
    public async Task<StartWorkflowResponse> StartWorkflowAsync(string definitionId)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowStarter>().StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published),
        });
    }

    /// <summary>
    /// Waits until the trigger hook has sent every event queued so far, and those the workflows they ran queued in
    /// turn. The hook sends in the background, so tests call it after their writes.
    /// </summary>
    public Task DeliveredAsync() =>
        services.GetRequiredService<AsterLifecycleTriggerHook>().WhenAllSentAsync().WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>Creates a product, version 1, in the given Aster tenant.</summary>
    public async Task<Aster.Core.Models.Instances.Resource> CreateProductAsync(string resourceId, TenantScope? tenant = null) =>
        await Manager.CreateAsync("Product", new CreateResourceRequest
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            InitialAspects = JsonDocument.Parse("""{ "Title": { "Title": "Gadget" } }""").RootElement
                .EnumerateObject().ToDictionary(static p => p.Name, static p => (object)p.Value.Clone()),
        });

    public ValueTask DisposeAsync() => services.DisposeAsync();
}

/// <summary>The workflow runs the trigger workflows recorded, by workflow, with the event their trigger received.</summary>
internal sealed class TriggerLog
{
    private readonly ConcurrentQueue<(string Workflow, ResourceLifecycleEvent? Event)> runs = new();

    public void Add(string workflow, ResourceLifecycleEvent? lifecycleEvent) => runs.Enqueue((workflow, lifecycleEvent));

    public IReadOnlyList<ResourceLifecycleEvent?> Of(string workflow) =>
        runs.Where(run => run.Workflow == workflow).Select(static run => run.Event).ToList();

    public IReadOnlyList<string> Workflows => runs.Select(static run => run.Workflow).ToList();
}

/// <summary>The stimuli the trigger hook sent, by activity type name.</summary>
internal sealed class SendLog
{
    private readonly ConcurrentQueue<(string ActivityTypeName, object Stimulus, StimulusMetadata? Metadata)> sends = new();

    public void Add(string activityTypeName, object stimulus, StimulusMetadata? metadata) => sends.Enqueue((activityTypeName, stimulus, metadata));

    public IReadOnlyList<(string ActivityTypeName, object Stimulus, StimulusMetadata? Metadata)> All => sends.ToList();
}

internal sealed class RecordingStimulusSender(IStimulusSender inner, SendLog log) : IStimulusSender
{
    public Task<SendStimulusResult> SendAsync(string activityTypeName, object stimulus, StimulusMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        log.Add(activityTypeName, stimulus, metadata);
        return inner.SendAsync(activityTypeName, stimulus, metadata, cancellationToken);
    }

    public Task<SendStimulusResult> SendAsync(string stimulusHash, StimulusMetadata? metadata = null, CancellationToken cancellationToken = default) =>
        inner.SendAsync(stimulusHash, metadata, cancellationToken);
}

internal sealed class FixedTenantsProvider(IReadOnlyList<string> tenantIds) : ITenantsProvider
{
    public Task<IEnumerable<Tenant>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(tenantIds.Select(static id => new Tenant { Id = id, Name = id }));

    public async Task<Tenant?> FindAsync(TenantFilter filter, CancellationToken cancellationToken = default) =>
        filter.Apply((await ListAsync(cancellationToken)).AsQueryable()).FirstOrDefault();
}

/// <summary>Collects log entries for assertions.</summary>
internal sealed class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Category, string Message, Exception? Exception)> entries = new();

    public IReadOnlyList<(LogLevel Level, string Category, string Message, Exception? Exception)> Entries => entries.ToList();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.entries.Enqueue((logLevel, category, formatter(state, exception), exception));
    }
}

/// <summary>Records the event a trigger received, under the workflow's name.</summary>
public sealed class RecordRun : CodeActivity
{
    public RecordRun(string workflow, Variable<ResourceLifecycleEvent> lifecycleEvent)
    {
        Workflow = workflow;
        Event = new(lifecycleEvent);
    }

    public string Workflow { get; set; }

    public Input<ResourceLifecycleEvent?> Event { get; set; }

    protected override void Execute(ActivityExecutionContext context) =>
        context.GetRequiredService<TriggerLog>().Add(Workflow, context.Get(Event));
}

/// <summary>A workflow started by <typeparamref name="TTrigger"/>, which records the event it received.</summary>
public abstract class TriggerWorkflow<TTrigger> : WorkflowBase where TTrigger : ResourceLifecycleTrigger
{
    protected abstract string Name { get; }

    protected abstract TTrigger CreateTrigger();

    protected virtual void Configure(TTrigger trigger)
    {
    }

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(Name);
        var lifecycleEvent = builder.WithVariable<ResourceLifecycleEvent>("Event", default!);
        var trigger = CreateTrigger();
        trigger.CanStartWorkflow = true;
        trigger.Result = new(lifecycleEvent);
        Configure(trigger);
        builder.Root = new Sequence { Activities = { trigger, new RecordRun(Name, lifecycleEvent) } };
    }
}

public sealed class OnProductPublished : TriggerWorkflow<ResourceActivated>
{
    protected override ResourceActivated CreateTrigger() => new();

    public const string Id = "on-product-published";

    protected override string Name => Id;

    protected override void Configure(ResourceActivated trigger)
    {
        trigger.DefinitionId = new("Product");
        trigger.Channel = new("Published");
    }
}

public sealed class OnAnyActivation : TriggerWorkflow<ResourceActivated>
{
    protected override ResourceActivated CreateTrigger() => new();

    public const string Id = "on-any-activation";

    protected override string Name => Id;
}

public sealed class OnProductDrafted : TriggerWorkflow<ResourceActivated>
{
    protected override ResourceActivated CreateTrigger() => new();

    public const string Id = "on-product-drafted";

    protected override string Name => Id;

    protected override void Configure(ResourceActivated trigger)
    {
        trigger.DefinitionId = new("Product");
        trigger.Channel = new("Draft");
    }
}

public sealed class OnOtherDefinitionActivated : TriggerWorkflow<ResourceActivated>
{
    protected override ResourceActivated CreateTrigger() => new();

    public const string Id = "on-other-definition-activated";

    protected override string Name => Id;

    protected override void Configure(ResourceActivated trigger) => trigger.DefinitionId = new("Other");
}

public sealed class OnProductSaved : TriggerWorkflow<ResourceSaved>
{
    protected override ResourceSaved CreateTrigger() => new();

    public const string Id = "on-product-saved";

    protected override string Name => Id;

    protected override void Configure(ResourceSaved trigger) => trigger.DefinitionId = new("Product");
}

public sealed class OnProductUnpublished : TriggerWorkflow<ResourceDeactivated>
{
    protected override ResourceDeactivated CreateTrigger() => new();

    public const string Id = "on-product-unpublished";

    protected override string Name => Id;

    protected override void Configure(ResourceDeactivated trigger)
    {
        trigger.DefinitionId = new("Product");
        trigger.Channel = new("Published");
    }
}

public sealed class OnProductArchived : TriggerWorkflow<ResourceArchived>
{
    protected override ResourceArchived CreateTrigger() => new();

    public const string Id = "on-product-archived";

    protected override string Name => Id;

    protected override void Configure(ResourceArchived trigger) => trigger.DefinitionId = new("Product");
}

/// <summary>Started explicitly; waits on a Resource Saved bookmark, then records the event that resumed it.</summary>
public sealed class WaitForProductSaved : WorkflowBase
{
    public const string Id = "wait-for-product-saved";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(Id);
        var lifecycleEvent = builder.WithVariable<ResourceLifecycleEvent>("Event", default!);
        builder.Root = new Sequence
        {
            Activities =
            {
                new ResourceSaved { DefinitionId = new("Product"), Result = new(lifecycleEvent) },
                new RecordRun(Id, lifecycleEvent),
            },
        };
    }
}

/// <summary>
/// Started explicitly; waits on a Resource Saved bookmark for products, then, still in the resumed run, creates another
/// product, whose event is sent with the same stimulus.
/// </summary>
public sealed class WaitThenCreateProduct : WorkflowBase
{
    public const string Id = "wait-then-create-product";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(Id);
        var lifecycleEvent = builder.WithVariable<ResourceLifecycleEvent>("Event", default!);
        builder.Root = new Sequence
        {
            Activities =
            {
                new ResourceSaved { DefinitionId = new("Product"), Result = new(lifecycleEvent) },
                new CreateResource { DefinitionId = new("Product"), ResourceId = new("created-by-workflow"), Aspects = new("""{ "Title": { "Title": "Gadget" } }""") },
                new RecordRun(Id, lifecycleEvent),
            },
        };
    }
}

/// <summary>
/// Started by a product save, then waits on a second Resource Saved trigger (unfiltered unless
/// <see cref="SecondDefinitionId"/> is set), and records the event each trigger received.
/// </summary>
public abstract class StartThenWait : WorkflowBase
{
    protected abstract string Name { get; }

    protected virtual string? SecondDefinitionId => null;

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(Name);
        var first = builder.WithVariable<ResourceLifecycleEvent>("First", default!);
        var second = builder.WithVariable<ResourceLifecycleEvent>("Second", default!);
        builder.Root = new Sequence
        {
            Activities =
            {
                new ResourceSaved { DefinitionId = new("Product"), CanStartWorkflow = true, Result = new(first) },
                new RecordRun(Name + ":first", first),
                new ResourceSaved { DefinitionId = new(SecondDefinitionId), Result = new(second) },
                new RecordRun(Name + ":second", second),
            },
        };
    }
}

public sealed class StartThenWaitForAnySave : StartThenWait
{
    public const string Id = "start-then-wait-for-any-save";

    protected override string Name => Id;
}

public sealed class StartThenWaitForProductSave : StartThenWait
{
    public const string Id = "start-then-wait-for-product-save";

    protected override string Name => Id;

    protected override string? SecondDefinitionId => "Product";
}
