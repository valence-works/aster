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
/// </remarks>
public abstract class ResourceLifecycleTrigger : Trigger<ResourceLifecycleEvent>
{
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

        context.CreateBookmark(new CreateBookmarkArgs
        {
            Stimulus = Stimulus(context.ExpressionExecutionContext),
            IncludeActivityInstanceId = false,
            Callback = OnEventReceivedAsync,
        });
    }

    private ResourceLifecycleStimulus Stimulus(ExpressionExecutionContext context) =>
        new(
            context.ServiceProvider.GetService<ITenantAccessor>()?.TenantId ?? global::Elsa.Common.Multitenancy.Tenant.DefaultTenantId,
            Filter(context.Get(DefinitionId)),
            Filter(context.Get(ChannelFilter)));

    private static string? Filter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private async ValueTask OnEventReceivedAsync(ActivityExecutionContext context)
    {
        context.TryGetWorkflowInput<ResourceLifecycleEvent>(ResourceLifecycleEvent.WorkflowInputKey, out var lifecycleEvent);
        context.Set(Result, lifecycleEvent);
        await context.CompleteActivityAsync();
    }
}
