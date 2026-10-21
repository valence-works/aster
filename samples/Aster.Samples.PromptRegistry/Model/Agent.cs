namespace Aster.Samples.PromptRegistry.Model;

/// <summary>
/// The single aspect of an <c>Agent</c> resource. <see cref="Prompt"/> and <see cref="Tools"/> hold
/// resource IDs and are declared as <c>reference</c> and <c>reference[]</c> facets, so Aster checks
/// on save that they point at an existing prompt and tools, and rewrites them when an import remaps
/// their targets.
/// </summary>
public sealed record Agent
{
    /// <summary>A display name.</summary>
    public required string Name { get; init; }

    /// <summary>The resource ID of the agent's prompt.</summary>
    public required string Prompt { get; init; }

    /// <summary>The resource IDs of the tools the agent may call.</summary>
    public string[] Tools { get; init; } = [];
}
