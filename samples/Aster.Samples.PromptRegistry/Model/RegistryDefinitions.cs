using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;

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
    public static ResourceDefinition PromptDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(PromptId)
        .WithAspect<Prompt>()
        .WithFacetMetadata(nameof(Prompt.Template), new FacetMetadata { MinLength = 1, UiHint = UiHints.Multiline })
        .WithFacetMetadata(nameof(Prompt.Variables), new FacetMetadata { UniqueItems = true })
        .Build();

    /// <summary>A tool; facets are derived from <see cref="Tool"/>.</summary>
    public static ResourceDefinition ToolDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(ToolId)
        .WithAspect<Tool>()
        .Build();

    /// <summary>
    /// An agent; facets are derived from <see cref="Agent"/>, whose <c>[ReferenceFacet]</c> properties
    /// become <c>reference</c> and <c>reference[]</c> facets with their target definitions.
    /// </summary>
    public static ResourceDefinition AgentDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(AgentId)
        .WithAspect<Agent>()
        .WithFacetMetadata(nameof(Agent.Tools), new FacetMetadata { UniqueItems = true })
        .Build();

    /// <summary>Registers the three definitions.</summary>
    public static async Task RegisterAsync(IResourceDefinitionStore definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        await definitions.RegisterDefinitionAsync(PromptDefinition(), cancellationToken);
        await definitions.RegisterDefinitionAsync(ToolDefinition(), cancellationToken);
        await definitions.RegisterDefinitionAsync(AgentDefinition(), cancellationToken);
    }
}
