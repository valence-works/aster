using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Common;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Archives a resource with an archive lifecycle marker, timestamped with Elsa's system clock. Versions and activations
/// are unchanged; Query Resources hides the resource unless it includes archived resources. A marker Aster refuses
/// faults with Aster's lifecycle marker code, such as <c>lifecycle-marker-conflict</c>. The result is the marker.
/// </summary>
[Activity("Aster", "Aster", "Archives an Aster resource. Queries then hide it unless they include archived resources.", DisplayName = "Archive Resource")]
public class ArchiveResource : CodeActivity<ResourceLifecycleMarker>
{
    /// <inheritdoc />
    public ArchiveResource([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the resource ID.</summary>
    [Input(Description = "The resource ID.")]
    public Input<string> ResourceId { get; set; } = default!;

    /// <summary>Gets or sets the reason recorded with the marker.</summary>
    [Input(Description = "The reason recorded with the archive marker.")]
    public Input<string?>? Reason { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var marker = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var resourceId = AsterActivityContext.Required(context.Get(ResourceId), AsterActivityFaultCodes.ResourceIdRequired, nameof(ResourceId));
            await AsterActivityContext.RequireLatestAsync(context.GetRequiredService<IResourceManager>(), resourceId, tenant, context.CancellationToken);

            var result = await context.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                TenantScope = tenant,
                ResourceId = resourceId,
                State = ResourceLifecycleMarkerState.Archived,
                MarkedAt = context.GetService<ISystemClock>()?.UtcNow ?? DateTimeOffset.UtcNow,
                Reason = context.Get(Reason),
            }, context.CancellationToken);

            if (!result.Succeeded || result.Marker is null)
                throw new AsterActivityException(
                    result.Diagnostics.Count == 0
                        ? [new AsterActivityDiagnostic(AsterActivityFaultCodes.ResourceNotFound, $"Resource '{resourceId}' could not be archived.")]
                        : result.Diagnostics.Select(static d => new AsterActivityDiagnostic(d.Code, d.Message, Path: d.Path)).ToList(),
                    $"Resource '{resourceId}' could not be archived.");

            return result.Marker;
        });

        context.Set(Result, marker);
    }
}
