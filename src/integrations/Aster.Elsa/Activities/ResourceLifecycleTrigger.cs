using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using ITenantAccessor = Elsa.Common.Multitenancy.ITenantAccessor;

namespace Aster.Elsa.Activities;

/// <summary>
/// The shared behaviour of the Aster lifecycle triggers. As a workflow's start activity (with
/// <c>CanStartWorkflow</c> set) a trigger starts a new workflow instance for each matching event. Anywhere else it waits
/// with a bookmark and resumes once, on the next matching event. Either way the result is the
/// <see cref="ResourceLifecycleEvent"/>.
/// </summary>
/// <remarks>
/// Filters are read when Elsa indexes the trigger (when the workflow is published) and when the bookmark is created;
/// an empty filter matches every value. A trigger only matches events from the Elsa tenant its workflow runs in.
/// Delivery is at-most-once: see the package README.
/// <para>
/// One event reaches a workflow instance through several stimuli (one per filter combination), and Elsa's bookmark
/// queue can deliver a stimulus again later. So the instance remembers the events its triggers received, and a
/// bookmark ignores the events its instance had already received when it was created: it waits on for the next one.
/// </para>
/// </remarks>
public abstract class ResourceLifecycleTrigger : Trigger<ResourceLifecycleEvent>
{
    /// <summary>The workflow property with the operation IDs of the events the instance's triggers received.</summary>
    private const string ReceivedKey = "Aster.ReceivedOperationIds";

    /// <summary>The activity property with the operation IDs a bookmark ignores: those received before it was created.</summary>
    private const string IgnoredKey = "Aster.IgnoredOperationIds";

    /// <summary>
    /// How many received operation IDs an instance keeps. A repeated stimulus arrives right after the original or from
    /// Elsa's bookmark queue within its time-to-live, so only recent events need remembering.
    /// </summary>
    private const int ReceivedLimit = 32;

    private protected ResourceLifecycleTrigger(string? source, int? line)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the definition ID filter, such as <c>Product</c>. Empty matches every definition.</summary>
    [Input(Description = "Only resources of this definition, such as Product. Leave empty for every definition.")]
    public Input<string?>? DefinitionId { get; set; }

    /// <summary>The channel filter of the activation triggers; the others have none.</summary>
    private protected virtual Input<string?>? ChannelFilter => null;

    /// <inheritdoc />
    protected override object GetTriggerPayload(TriggerIndexingContext context) =>
        Stimulus(context.ExpressionExecutionContext);

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        if (context.IsTriggerOfWorkflow())
        {
            await OnEventReceivedAsync(context);
            return;
        }

        if (context.WorkflowExecutionContext.GetProperty<string[]>(ReceivedKey) is { Length: > 0 } received)
            context.SetProperty(IgnoredKey, received);
        CreateBookmark(context);
    }

    private void CreateBookmark(ActivityExecutionContext context) =>
        context.CreateBookmark(new CreateBookmarkArgs
        {
            Stimulus = Stimulus(context.ExpressionExecutionContext),
            IncludeActivityInstanceId = false,
            Callback = OnEventReceivedAsync,
        });

    private ResourceLifecycleStimulus Stimulus(ExpressionExecutionContext context) =>
        new(
            context.ServiceProvider.GetService<ITenantAccessor>()?.TenantId ?? global::Elsa.Common.Multitenancy.Tenant.DefaultTenantId,
            Filter(context.Get(DefinitionId)),
            Filter(context.Get(ChannelFilter)));

    private static string? Filter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private async ValueTask OnEventReceivedAsync(ActivityExecutionContext context)
    {
        context.TryGetWorkflowInput<ResourceLifecycleEvent>(ResourceLifecycleEvent.WorkflowInputKey, out var lifecycleEvent);
        if (lifecycleEvent is { OperationId: var operationId } && operationId != Guid.Empty)
        {
            var id = operationId.ToString("D");
            if (context.GetProperty<string[]>(IgnoredKey) is { } ignored && ignored.Contains(id))
            {
                // Elsa burns the bookmark that resumed this callback: wait again, for the next event.
                CreateBookmark(context);
                return;
            }

            context.WorkflowExecutionContext.UpdateProperty<string[]>(ReceivedKey, received =>
                [.. (received ?? []).Append(id).TakeLast(ReceivedLimit)]);
        }

        context.Set(Result, lifecycleEvent);
        await context.CompleteActivityAsync();
    }
}
