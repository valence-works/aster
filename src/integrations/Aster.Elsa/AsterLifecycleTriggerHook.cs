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
/// Feeds the Aster lifecycle triggers: after each committed save, activation, deactivation and archive, sends Elsa the
/// matching stimuli through <see cref="IStimulusSender"/>, which starts trigger-bound workflows and resumes bookmarks.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only committed changes are sent: an after hook whose <see cref="ResourceLifecycleContext.OperationStatus"/>
/// is not <see cref="LifecycleOperationStatus.Succeeded"/> sends nothing.</item>
/// <item>Each event is sent in the Elsa tenant of the Aster write (<see cref="AsterTenantMapping.ToElsaTenantId"/>),
/// pushed with <see cref="ITenantAccessor.PushContext"/>. An Aster tenant with no Elsa tenant sends nothing.</item>
/// <item>One stimulus per combination of filled and empty filters (<see cref="ResourceLifecycleStimulus"/>), so each
/// matching trigger fires once.</item>
/// <item>Delivery is at-most-once. A failed send is logged as a warning with the resource, version, channel and
/// tenant, and never rethrown: the Aster write is committed and its caller and other hooks don't see the failure.
/// Nothing retries, because a retry could start a workflow twice. Sending isn't tied to the Aster caller's
/// cancellation token, since the change it reports is already committed.</item>
/// </list>
/// </remarks>
internal sealed partial class AsterLifecycleTriggerHook(IServiceScopeFactory scopeFactory, ILogger<AsterLifecycleTriggerHook> logger)
    : ResourceLifecycleHook
{
    private static readonly string SavedType = ActivityTypeNameHelper.GenerateTypeName<ResourceSaved>();
    private static readonly string ActivatedType = ActivityTypeNameHelper.GenerateTypeName<ResourceActivated>();
    private static readonly string DeactivatedType = ActivityTypeNameHelper.GenerateTypeName<ResourceDeactivated>();
    private static readonly string ArchivedType = ActivityTypeNameHelper.GenerateTypeName<ResourceArchived>();

    /// <inheritdoc />
    public override async ValueTask OnAfterSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.OperationStatus != LifecycleOperationStatus.Succeeded || context.Resource is not { } resource)
            return;

        await SendAsync(SavedType, context, new ResourceLifecycleEvent
        {
            Kind = ResourceLifecycleEventKind.Saved,
            ResourceId = resource.ResourceId,
            DefinitionId = resource.DefinitionId,
            ResourceVersion = resource.Version,
            SaveKind = context.SaveKind,
        }, hasChannelFilter: false);
    }

    /// <inheritdoc />
    public override async ValueTask OnAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.OperationStatus != LifecycleOperationStatus.Succeeded)
            return;

        await SendAsync(ActivatedType, context, ActivationEvent(ResourceLifecycleEventKind.Activated, context), hasChannelFilter: true);
    }

    /// <inheritdoc />
    public override async ValueTask OnAfterDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.OperationStatus != LifecycleOperationStatus.Succeeded)
            return;

        await SendAsync(DeactivatedType, context, ActivationEvent(ResourceLifecycleEventKind.Deactivated, context), hasChannelFilter: true);
    }

    /// <inheritdoc />
    public override async ValueTask OnAfterArchiveAsync(ResourceArchiveLifecycleContext context, CancellationToken cancellationToken = default)
    {
        // The archive points also cover soft-deletes, which Resource Archived doesn't report.
        if (context.OperationStatus != LifecycleOperationStatus.Succeeded || context.Marker.State != ResourceLifecycleMarkerState.Archived)
            return;

        await SendAsync(ArchivedType, context, new ResourceLifecycleEvent
        {
            Kind = ResourceLifecycleEventKind.Archived,
            ResourceId = context.ResourceId,
            DefinitionId = context.DefinitionId,
        }, hasChannelFilter: false);
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

    private async ValueTask SendAsync(string activityTypeName, ResourceLifecycleContext context, ResourceLifecycleEvent lifecycleEvent, bool hasChannelFilter)
    {
        var tenantId = AsterTenantMapping.ToElsaTenantId(context.TenantScope);
        lifecycleEvent = lifecycleEvent with { TenantId = tenantId, OperationId = context.OperationId };

        // The change is committed: deliver the event even when the Aster caller has since gone away.
        var cancellationToken = CancellationToken.None;
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
}
