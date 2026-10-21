using Aster.Core.Definitions;

namespace Aster.Samples.PromptRegistry.Model;

/// <summary>
/// The single aspect of an <c>Agent</c> resource. <see cref="Prompt"/> and <see cref="Tools"/> hold
/// resource IDs and are marked <see cref="ReferenceFacetAttribute"/>, so they derive <c>reference</c> and
/// <c>reference[]</c> facets: Aster checks on save that they point at an existing prompt and tools, and
/// rewrites them when an import remaps their targets.
/// </summary>
public sealed record Agent
{
    /// <summary>A display name.</summary>
    public required string Name { get; init; }

    /// <summary>The resource ID of the agent's prompt.</summary>
    [ReferenceFacet(RegistryDefinitions.PromptId)]
    public required string Prompt { get; init; }

    /// <summary>The resource IDs of the tools the agent may call.</summary>
    [ReferenceFacet(RegistryDefinitions.ToolId)]
    public string[] Tools { get; init; } = [];
}
