using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Portability;

namespace Aster.Samples.PromptRegistry.Model;

/// <summary>
/// The resource definitions of the registry. Each definition has one aspect whose key equals the
/// definition ID.
/// </summary>
public static class RegistryDefinitions
{
    /// <summary>Definition ID and aspect key of prompts.</summary>
    public const string PromptId = nameof(Prompt);

    /// <summary>Definition ID and aspect key of tools.</summary>
    public const string ToolId = nameof(Tool);

    /// <summary>Definition ID and aspect key of agents.</summary>
    public const string AgentId = nameof(Agent);

    /// <summary>A prompt; facets are derived from <see cref="Prompt"/>.</summary>
    public static ResourceDefinition PromptDefinition() => WithStableIds(new ResourceDefinitionBuilder()
        .WithDefinitionId(PromptId)
        .WithAspect<Prompt>()
        .WithFacetMetadata(nameof(Prompt.Template), new FacetMetadata { MinLength = 1, UiHint = UiHints.Multiline })
        .WithFacetMetadata(nameof(Prompt.Variables), new FacetMetadata { UniqueItems = true })
        .Build());

    /// <summary>A tool; facets are derived from <see cref="Tool"/>.</summary>
    public static ResourceDefinition ToolDefinition() => WithStableIds(new ResourceDefinitionBuilder()
        .WithDefinitionId(ToolId)
        .WithAspect<Tool>()
        .Build());

    /// <summary>
    /// An agent. Facets are declared one by one, because Aster never infers references: a string
    /// property derives a <c>string</c> facet, and a derived facet's type cannot be changed.
    /// </summary>
    public static ResourceDefinition AgentDefinition() => WithStableIds(new ResourceDefinitionBuilder()
        .WithDefinitionId(AgentId)
        .WithAspect(AgentId)
        .WithFacet(nameof(Agent.Name), "string", isRequired: true)
        .WithFacet(nameof(Agent.Prompt), "reference", isRequired: true)
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = [PromptId] })
        .WithFacet(nameof(Agent.Tools), "reference[]")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = [ToolId], UniqueItems = true })
        .Build());

    /// <summary>
    /// Replaces the random IDs <see cref="ResourceDefinitionBuilder"/> gives the definition, its
    /// aspects and its facets with IDs derived from their names.
    /// </summary>
    /// <remarks>
    /// Workaround for an SDK gap. Each environment registers these definitions from this code at
    /// startup, and import compares definitions including those generated IDs. Without stable IDs,
    /// identical definitions in two environments look divergent, and a
    /// <see cref="PortableImportCollisionMode.RemapDivergent"/> import moves the imported resources
    /// to new definitions such as <c>Prompt__imported</c>. Raise the revision when a definition's
    /// content changes.
    /// </remarks>
    private static ResourceDefinition WithStableIds(ResourceDefinition definition, int revision = 1) => definition with
    {
        Id = $"{definition.DefinitionId}@{revision}",
        AspectDefinitions = definition.AspectDefinitions.ToDictionary(
            static pair => pair.Key,
            pair => pair.Value with
            {
                Id = $"{definition.DefinitionId}@{revision}/{pair.Key}",
                FacetDefinitions = pair.Value.FacetDefinitions
                    .Select(facet => facet with { Id = $"{definition.DefinitionId}@{revision}/{pair.Key}/{facet.FacetDefinitionId}" })
                    .ToList(),
            }),
    };

    /// <summary>Registers the three definitions.</summary>
    public static async Task RegisterAsync(IResourceDefinitionStore definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        await definitions.RegisterDefinitionAsync(PromptDefinition(), cancellationToken);
        await definitions.RegisterDefinitionAsync(ToolDefinition(), cancellationToken);
        await definitions.RegisterDefinitionAsync(AgentDefinition(), cancellationToken);
    }
}
