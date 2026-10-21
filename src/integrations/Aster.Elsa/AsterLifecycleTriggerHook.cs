using System.Threading.Channels;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Elsa.Activities;
using Elsa.Workflows.Helpers;
using Elsa.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ElsaTenant = Elsa.Common.Multitenancy.Tenant;
using ITenantAccessor = Elsa.Common.Multitenancy.ITenantAccessor;
using ITenantFinder = Elsa.Common.Multitenancy.ITenantFinder;

namespace Aster.Elsa;

/// <summary>
/// Feeds the Aster lifecycle triggers: after each committed save, activation, deactivation and archive, queues the
/// event for a background worker that sends Elsa the matching stimuli through <see cref="IStimulusSender"/>, which
/// starts trigger-bound workflows and resumes bookmarks.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only committed changes are sent: an after hook whose <see cref="ResourceLifecycleContext.OperationStatus"/>
/// is not <see cref="LifecycleOperationStatus.Succeeded"/> queues nothing.</item>
/// <item>Events are sent in the background, never in the flow of the Aster call. Elsa holds a lock per stimulus while
/// it resumes the matching bookmarks, so a resumed workflow whose own write sent the same stimulus inline would wait on
/// that lock until it timed out. One worker sends the events in the order they were queued, one at a time, outside the
/// execution context of the Aster call. It starts with the hook and stops when the container disposes it.</item>
/// <item>Each event is sent in the Elsa tenant of the Aster write (<see cref="AsterTenantMapping.ToElsaTenantId"/>),
/// pushed with <see cref="ITenantAccessor.PushContext"/>. An Aster tenant with no Elsa tenant sends nothing.</item>
/// <item>One stimulus per combination of filled and empty filters (<see cref="ResourceLifecycleStimulus"/>), so each
/// matching trigger fires once.</item>
/// <item>Delivery is at-most-once. A failed send is logged as a warning with the resource, version, channel and
/// tenant, and never rethrown: the Aster write is committed and its caller and other hooks don't see the failure.
/// Nothing retries, because a retry could start a workflow twice. The queue is in memory, so events still queued when
/// the process stops are lost. Sending isn't tied to the Aster caller's cancellation token, since the change it
/// reports is already committed; only stopping the worker cancels it.</item>
/// </list>
/// </remarks>
internal sealed partial class AsterLifecycleTriggerHook : ResourceLifecycleHook, IAsyncDisposable
{
    private static readonly string SavedType = ActivityTypeNameHelper.GenerateTypeName<ResourceSaved>();
    private static readonly string ActivatedType = ActivityTypeNameHelper.GenerateTypeName<ResourceActivated>();
    private static readonly string DeactivatedType = ActivityTypeNameHelper.GenerateTypeName<ResourceDeactivated>();
    private static readonly string ArchivedType = ActivityTypeNameHelper.GenerateTypeName<ResourceArchived>();

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<AsterLifecycleTriggerHook> logger;
    private readonly Channel<QueuedEvent> queue = Channel.CreateUnbounded<QueuedEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    private readonly object gate = new();
    private int unsent;
    private int disposed;
    private TaskCompletionSource allSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AsterLifecycleTriggerHook(IServiceScopeFactory scopeFactory, ILogger<AsterLifecycleTriggerHook> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        allSent.SetResult();

        // The worker must not inherit the execution context (Elsa's tenant, a running workflow) of the Aster call
        // that happened to resolve the hook first.
        if (ExecutionContext.IsFlowSuppressed())
        {
            worker = Task.Run(SendQueuedAsync);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
                worker = Task.Run(SendQueuedAsync);
        }
    }

    /// <inheritdoc />
    public override ValueTask OnAfterSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.OperationStatus == LifecycleOperationStatus.Succeeded && context.Resource is { } resource)
        {
            Queue(SavedType, context, new ResourceLifecycleEvent
            {
                Kind = ResourceLifecycleEventKind.Saved,
                ResourceId = resource.ResourceId,
                DefinitionId = resource.DefinitionId,
                ResourceVersion = resource.Version,
                SaveKind = context.SaveKind,
            }, hasChannelFilter: false);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask OnAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.OperationStatus == LifecycleOperationStatus.Succeeded)
            Queue(ActivatedType, context, ActivationEvent(ResourceLifecycleEventKind.Activated, context), hasChannelFilter: true);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask OnAfterDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.OperationStatus == LifecycleOperationStatus.Succeeded)
            Queue(DeactivatedType, context, ActivationEvent(ResourceLifecycleEventKind.Deactivated, context), hasChannelFilter: true);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask OnAfterArchiveAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default)
    {
        // The archive points also cover soft-deletes, which Resource Archived doesn't report.
        if (context.OperationStatus == LifecycleOperationStatus.Succeeded && context.Marker.State == ResourceLifecycleMarkerState.Archived)
        {
            Queue(ArchivedType, context, new ResourceLifecycleEvent
            {
                Kind = ResourceLifecycleEventKind.Archived,
                ResourceId = context.ResourceId,
                DefinitionId = context.DefinitionId,
            }, hasChannelFilter: false);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Completes when every event queued so far has been sent or has failed, including the events that the workflows
    /// they ran queued in turn. Tests use it to wait for delivery.
    /// </summary>
    internal Task WhenAllSentAsync()
    {
        lock (gate)
            return allSent.Task;
    }

    /// <summary>Stops the worker. Events still queued are not sent.</summary>
    public async ValueTask DisposeAsync()
    {
        // The container disposes the hook once for each registration that resolves it.
        if (Interlocked.Exchange(ref disposed, 1) == 1)
            return;

        queue.Writer.TryComplete();
        await stopping.CancelAsync();
        await worker;
        stopping.Dispose();
    }

    /// <summary>
    /// The stimuli for an event: every combination of the event's definition ID and channel with an empty filter
    /// (<see langword="null"/>), so a trigger matches whichever combination its own filters make.
    /// </summary>
    internal static IEnumerable<ResourceLifecycleStimulus> Stimuli(string tenantId, string? definitionId, string? channel, bool hasChannelFilter)
    {
        string?[] definitionIds = definitionId is null ? [null] : [definitionId, null];
        string?[] channels = hasChannelFilter && channel is not null ? [channel, null] : [null];
        foreach (var definition in definitionIds)
        {
            foreach (var channelFilter in channels)
                yield return new ResourceLifecycleStimulus(tenantId, definition, channelFilter);
        }
    }

    private static ResourceLifecycleEvent ActivationEvent(ResourceLifecycleEventKind kind, ResourceActivationLifecycleContext context) =>
        new()
        {
            Kind = kind,
            ResourceId = context.ResourceId,
            DefinitionId = context.DefinitionId,
            ResourceVersion = context.Version,
            Channel = context.Channel,
        };

    private void Queue(string activityTypeName, ResourceLifecycleContext context, ResourceLifecycleEvent lifecycleEvent, bool hasChannelFilter)
    {
        lifecycleEvent = lifecycleEvent with { TenantId = AsterTenantMapping.ToElsaTenantId(context.TenantScope), OperationId = context.OperationId };
        lock (gate)
        {
            if (unsent++ == 0)
                allSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Only a disposed hook refuses the event.
        if (!queue.Writer.TryWrite(new QueuedEvent(activityTypeName, lifecycleEvent, hasChannelFilter)))
            MarkSent();
    }

    private void MarkSent()
    {
        lock (gate)
        {
            if (--unsent == 0)
                allSent.TrySetResult();
        }
    }

    private async Task SendQueuedAsync()
    {
        try
        {
            await foreach (var queued in queue.Reader.ReadAllAsync(stopping.Token))
            {
                try
                {
                    await SendAsync(queued.ActivityTypeName, queued.Event, queued.HasChannelFilter, stopping.Token);
                }
                finally
                {
                    MarkSent();
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private async Task SendAsync(string activityTypeName, ResourceLifecycleEvent lifecycleEvent, bool hasChannelFilter, CancellationToken cancellationToken)
    {
        var tenantId = lifecycleEvent.TenantId;
        try
        {
            var tenant = await FindTenantAsync(tenantId, cancellationToken);
            if (tenant is null)
            {
                LogNoElsaTenant(activityTypeName, lifecycleEvent.ResourceId, tenantId);
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            using var tenantContext = scope.ServiceProvider.GetService<ITenantAccessor>()?.PushContext(tenant);
            var sender = scope.ServiceProvider.GetRequiredService<IStimulusSender>();
            var metadata = new StimulusMetadata
            {
                Input = new Dictionary<string, object> { [ResourceLifecycleEvent.WorkflowInputKey] = lifecycleEvent },
            };

            foreach (var stimulus in Stimuli(tenantId, lifecycleEvent.DefinitionId, lifecycleEvent.Channel, hasChannelFilter))
            {
                try
                {
                    await sender.SendAsync(activityTypeName, stimulus, metadata, cancellationToken);
                }
                catch (Exception exception)
                {
                    LogSendFailed(exception, activityTypeName, lifecycleEvent);
                }
            }
        }
        catch (Exception exception)
        {
            LogSendFailed(exception, activityTypeName, lifecycleEvent);
        }
    }

    /// <summary>
    /// The Elsa tenant to send in, from Elsa's tenants provider. Elsa's default tenant is assumed to exist, as Elsa
    /// itself does when its provider lists no tenants.
    /// </summary>
    private async ValueTask<ElsaTenant?> FindTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var finder = scope.ServiceProvider.GetService<ITenantFinder>();
        var tenant = finder is null ? null : await finder.FindByIdAsync(tenantId, cancellationToken);
        return tenant ?? (tenantId == ElsaTenant.DefaultTenantId ? ElsaTenant.Default : null);
    }

    private void LogSendFailed(Exception exception, string activityTypeName, ResourceLifecycleEvent lifecycleEvent) =>
        LogSendFailed(
            exception,
            activityTypeName,
            lifecycleEvent.ResourceId,
            lifecycleEvent.ResourceVersion,
            lifecycleEvent.Channel,
            lifecycleEvent.TenantId,
            lifecycleEvent.DefinitionId);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Could not send the {Trigger} event of resource '{ResourceId}' version {ResourceVersion} (channel '{Channel}', Elsa tenant '{TenantId}', definition '{DefinitionId}') to Elsa. The Aster change is committed; the event is not retried.")]
    private partial void LogSendFailed(Exception exception, string trigger, string resourceId, int? resourceVersion, string? channel, string tenantId, string? definitionId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "No {Trigger} event sent for resource '{ResourceId}': Elsa has no tenant '{TenantId}' for its Aster tenant.")]
    private partial void LogNoElsaTenant(string trigger, string resourceId, string tenantId);

    /// <summary>An event waiting for the worker, with the trigger it is for.</summary>
    private readonly record struct QueuedEvent(string ActivityTypeName, ResourceLifecycleEvent Event, bool HasChannelFilter);
}
