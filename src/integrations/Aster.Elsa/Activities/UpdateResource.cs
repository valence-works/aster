using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;

namespace Aster.Elsa.Activities;

/// <summary>
/// Saves a new version of a resource, based on <see cref="BaseVersion"/>. When the latest version is no longer
/// <see cref="BaseVersion"/>, the activity faults with <c>concurrency-conflict</c> and writes nothing. The result is
/// the new resource version.
/// </summary>
[Activity("Aster", "Aster", "Saves a new version of an Aster resource. Faults with concurrency-conflict when the base version is no longer the latest.", DisplayName = "Update Resource")]
public class UpdateResource : CodeActivity<Resource>
{
    /// <inheritdoc />
    public UpdateResource([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the resource ID.</summary>
    [Input(Description = "The resource ID.")]
    public Input<string> ResourceId { get; set; } = default!;

    /// <summary>Gets or sets the latest version the edit is based on (optimistic concurrency).</summary>
    [Input(Description = "The latest version the edit is based on. A newer latest version faults with concurrency-conflict.")]
    public Input<int> BaseVersion { get; set; } = default!;

    /// <summary>
    /// Gets or sets the aspect payloads to replace, keyed by aspect key. Each entry replaces the whole aspect; aspects
    /// not named keep their values. Accepts the same shapes as <see cref="CreateResource.Aspects"/>.
    /// </summary>
    [Input(
        Description = "The aspect payloads to replace: a JSON object keyed by aspect key. Each entry replaces the whole aspect.",
        UIHint = InputUIHints.JsonEditor)]
    public Input<object?>? Aspects { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resource = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var resourceId = AsterActivityContext.Required(context.Get(ResourceId), AsterActivityFaultCodes.ResourceIdRequired, nameof(ResourceId));
            var aspects = AsterAspectPayload.ToAspects(context.Get(Aspects));

            var manager = context.GetRequiredService<IResourceManager>();
            await AsterActivityContext.RequireLatestAsync(manager, resourceId, tenant, context.CancellationToken);
            return await manager.UpdateAsync(resourceId, new UpdateResourceRequest
            {
                TenantScope = tenant,
                BaseVersion = context.Get(BaseVersion),
                AspectUpdates = aspects,
            }, context.CancellationToken);
        });

        context.Set(Result, resource);
    }
}
