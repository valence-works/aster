using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Promotes the version active in <see cref="SourceChannel"/> to <see cref="TargetChannel"/>: two explicit calls, a
/// read of the active version and an activation, as the Elsa spike decided (F7). It faults with
/// <c>promote-source-not-active</c> when no version is active in the source channel, and with
/// <c>promote-source-ambiguous</c> when several are. The result is the promoted version.
/// </summary>
[Activity("Aster", "Aster", "Promotes the Aster resource version active in one channel to another, such as staging to production.", DisplayName = "Promote Resource")]
public class PromoteResource : CodeActivity<Resource>
{
    /// <inheritdoc />
    public PromoteResource([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the resource ID.</summary>
    [Input(Description = "The resource ID.")]
    public Input<string> ResourceId { get; set; } = default!;

    /// <summary>Gets or sets the channel to promote from, such as <c>staging</c>.</summary>
    [Input(Description = "The channel to promote from, such as staging. Exactly one version must be active in it.")]
    public Input<string> SourceChannel { get; set; } = default!;

    /// <summary>Gets or sets the channel to promote to, such as <c>production</c>.</summary>
    [Input(Description = "The channel to promote to, such as production.")]
    public Input<string> TargetChannel { get; set; } = default!;

    /// <summary>Gets or sets whether to keep other versions active in the target channel.</summary>
    [Input(Description = "Keep other versions active in the target channel.")]
    public Input<bool>? AllowMultipleActive { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resource = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var resourceId = AsterActivityContext.Required(context.Get(ResourceId), AsterActivityFaultCodes.ResourceIdRequired, nameof(ResourceId));
            var sourceChannel = AsterActivityContext.Required(context.Get(SourceChannel), AsterActivityFaultCodes.ChannelRequired, nameof(SourceChannel));
            var targetChannel = AsterActivityContext.Required(context.Get(TargetChannel), AsterActivityFaultCodes.ChannelRequired, nameof(TargetChannel));

            var manager = context.GetRequiredService<IResourceManager>();
            await AsterActivityContext.RequireLatestAsync(manager, resourceId, tenant, context.CancellationToken);

            var active = (await manager.GetActiveVersionsAsync(resourceId, sourceChannel, tenant, context.CancellationToken))
                .OrderBy(static version => version.Version)
                .ToList();
            if (active.Count == 0)
                throw new AsterActivityException(
                    AsterActivityFaultCodes.PromoteSourceNotActive,
                    $"No version of resource '{resourceId}' is active in channel '{sourceChannel}'.");
            if (active.Count > 1)
                throw new AsterActivityException(
                    AsterActivityFaultCodes.PromoteSourceAmbiguous,
                    $"Versions {string.Join(", ", active.Select(static version => version.Version))} of resource '{resourceId}' are active in channel '{sourceChannel}'; activate the one to promote explicitly.");

            var promoted = active[0];
            await manager.ActivateAsync(resourceId, promoted.Version, targetChannel, tenant, context.Get(AllowMultipleActive), context.CancellationToken);
            return promoted;
        });

        context.Set(Result, resource);
    }
}
