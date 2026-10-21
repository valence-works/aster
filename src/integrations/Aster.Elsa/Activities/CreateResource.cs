using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;

namespace Aster.Elsa.Activities;

/// <summary>
/// Creates version 1 of a resource, in the Aster tenant mapped from the current Elsa tenant. The result is the created
/// resource version, as <c>POST /resources</c> of <c>Aster.AspNetCore</c> returns it.
/// </summary>
[Activity("Aster", "Aster", "Creates version 1 of an Aster resource from a JSON object of aspect payloads keyed by aspect key.", DisplayName = "Create Resource")]
public class CreateResource : CodeActivity<Resource>
{
    /// <inheritdoc />
    public CreateResource([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the ID of the resource definition.</summary>
    [Input(Description = "The ID of the resource definition, such as Product.")]
    public Input<string> DefinitionId { get; set; } = default!;

    /// <summary>Gets or sets the resource ID; Aster generates one when it is empty.</summary>
    [Input(Description = "The resource ID. Leave empty to have Aster generate one.")]
    public Input<string?>? ResourceId { get; set; }

    /// <summary>
    /// Gets or sets the aspect payloads: a JSON object keyed by aspect key, such as
    /// <c>{ "Title": { "Title": "Gadget" } }</c>. Accepts JSON text, a <c>JsonElement</c> or <c>JsonObject</c>, a
    /// dictionary or <c>ExpandoObject</c>, or any object that serializes to a JSON object.
    /// </summary>
    [Input(
        Description = "The aspect payloads: a JSON object keyed by aspect key, such as { \"Title\": { \"Title\": \"Gadget\" } }.",
        UIHint = InputUIHints.JsonEditor)]
    public Input<object?>? Aspects { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resource = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var definitionId = AsterActivityContext.Required(context.Get(DefinitionId), AsterActivityFaultCodes.DefinitionIdRequired, nameof(DefinitionId));
            var aspects = AsterAspectPayload.ToAspects(context.Get(Aspects));

            var definitions = context.GetRequiredService<IResourceDefinitionStore>();
            if (await definitions.GetDefinitionAsync(definitionId, tenant, context.CancellationToken) is null)
                throw new AsterActivityException(AsterActivityFaultCodes.DefinitionNotFound, $"Definition '{definitionId}' was not found.");

            var resourceId = context.Get(ResourceId);
            return await context.GetRequiredService<IResourceManager>().CreateAsync(definitionId, new CreateResourceRequest
            {
                TenantScope = tenant,
                ResourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId,
                InitialAspects = aspects,
            }, context.CancellationToken);
        });

        context.Set(Result, resource);
    }
}
