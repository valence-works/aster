using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Reads one version of a resource: the latest version, the version named by <see cref="ResourceVersion"/>, or the version
/// active in <see cref="Channel"/> (the highest one when several are active). Set at most one of
/// <see cref="ResourceVersion"/> and <see cref="Channel"/>. The result is <see langword="null"/> when there is no such
/// version; archived and soft-deleted resources are returned like any other, as a read by ID over HTTP does.
/// </summary>
[Activity("Aster", "Aster", "Reads an Aster resource: the latest version, a specific version, or the version active in a channel. The result is empty when there is none.", DisplayName = "Get Resource")]
public class GetResource : CodeActivity<Resource?>
{
    /// <inheritdoc />
    public GetResource([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the resource ID.</summary>
    [Input(Description = "The resource ID.")]
    public Input<string> ResourceId { get; set; } = default!;

    /// <summary>Gets or sets the version to read; the latest version when empty.</summary>
    [Input(Description = "The version to read. Leave empty for the latest version, or set Channel instead.")]
    public Input<int?>? ResourceVersion { get; set; }

    /// <summary>Gets or sets the channel whose active version to read, such as <c>Published</c>.</summary>
    [Input(Description = "The channel whose active version to read, such as Published. The highest version wins when several are active.")]
    public Input<string?>? Channel { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resource = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var resourceId = AsterActivityContext.Required(context.Get(ResourceId), AsterActivityFaultCodes.ResourceIdRequired, nameof(ResourceId));
            var version = context.Get(ResourceVersion);
            var channel = context.Get(Channel);
            if (string.IsNullOrWhiteSpace(channel))
                channel = null;
            if (version is not null && channel is not null)
                throw new AsterActivityException(AsterActivityFaultCodes.ResourceSelectorInvalid, "Set at most one of ResourceVersion and Channel.");

            var manager = context.GetRequiredService<IResourceManager>();
            if (version is { } specific)
                return await manager.GetVersionAsync(resourceId, specific, tenant, context.CancellationToken);
            if (channel is not null)
                return (await manager.GetActiveVersionsAsync(resourceId, channel, tenant, context.CancellationToken))
                    .OrderByDescending(static active => active.Version)
                    .FirstOrDefault();
            return await manager.GetLatestVersionAsync(resourceId, tenant, context.CancellationToken);
        });

        context.Set(Result, resource);
    }
}
