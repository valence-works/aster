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
    /// An agent. Facets are declared one by one, because Aster never infers references: a string
    /// property derives a <c>string</c> facet, and a derived facet's type cannot be changed.
    /// </summary>
    public static ResourceDefinition AgentDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(AgentId)
        .WithAspect(AgentId)
        .WithFacet(nameof(Agent.Name), "string", isRequired: true)
        .WithFacet(nameof(Agent.Prompt), "reference", isRequired: true)
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = [PromptId] })
        .WithFacet(nameof(Agent.Tools), "reference[]")
        .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = [ToolId], UniqueItems = true })
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
