using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Activates a resource version in a channel. Unless <see cref="AllowMultipleActive"/> is set, other versions active in
/// the channel are deactivated. Any existing version can be activated, including a historical one (a rollback). The
/// result is the activated version.
/// </summary>
[Activity("Aster", "Aster", "Activates an Aster resource version in a channel, such as Published.", DisplayName = "Activate Version")]
public class ActivateResourceVersion : CodeActivity<Resource>
{
    /// <inheritdoc />
    public ActivateResourceVersion([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the resource ID.</summary>
    [Input(Description = "The resource ID.")]
    public Input<string> ResourceId { get; set; } = default!;

    /// <summary>Gets or sets the version to activate.</summary>
    [Input(Description = "The version to activate.")]
    public Input<int> ResourceVersion { get; set; } = default!;

    /// <summary>Gets or sets the activation channel, such as <c>Published</c>.</summary>
    [Input(Description = "The activation channel, such as Published.")]
    public Input<string> Channel { get; set; } = default!;

    /// <summary>Gets or sets whether to keep other versions active in the channel.</summary>
    [Input(Description = "Keep other versions active in the channel, such as for an A/B test.")]
    public Input<bool>? AllowMultipleActive { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resource = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var resourceId = AsterActivityContext.Required(context.Get(ResourceId), AsterActivityFaultCodes.ResourceIdRequired, nameof(ResourceId));
            var channel = AsterActivityContext.Required(context.Get(Channel), AsterActivityFaultCodes.ChannelRequired, nameof(Channel));
            var version = context.Get(ResourceVersion);

            var manager = context.GetRequiredService<IResourceManager>();
            var target = await AsterActivityContext.RequireVersionAsync(manager, resourceId, version, tenant, context.CancellationToken);
            await manager.ActivateAsync(resourceId, version, channel, tenant, context.Get(AllowMultipleActive), context.CancellationToken);
            return target;
        });

        context.Set(Result, resource);
    }
}
