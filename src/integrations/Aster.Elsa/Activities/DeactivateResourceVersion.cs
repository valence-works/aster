using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Deactivates a resource version in a channel. Other versions active in the channel stay active. The result is the
/// deactivated version.
/// </summary>
[Activity("Aster", "Aster", "Deactivates an Aster resource version in a channel.", DisplayName = "Deactivate Version")]
public class DeactivateResourceVersion : CodeActivity<Resource>
{
    /// <inheritdoc />
    public DeactivateResourceVersion([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the resource ID.</summary>
    [Input(Description = "The resource ID.")]
    public Input<string> ResourceId { get; set; } = default!;

    /// <summary>Gets or sets the version to deactivate.</summary>
    [Input(Description = "The version to deactivate.")]
    public Input<int> ResourceVersion { get; set; } = default!;

    /// <summary>Gets or sets the activation channel, such as <c>Published</c>.</summary>
    [Input(Description = "The activation channel, such as Published.")]
    public Input<string> Channel { get; set; } = default!;

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
            await manager.DeactivateAsync(resourceId, version, channel, tenant, context.CancellationToken);
            return target;
        });

        context.Set(Result, resource);
    }
}
