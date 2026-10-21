namespace Aster.Samples.PromptRegistry.Model;

/// <summary>
/// The single aspect of a <c>Tool</c> resource: a capability an agent may call.
/// </summary>
public sealed record Tool
{
    /// <summary>The name the model calls the tool by.</summary>
    public required string Name { get; init; }

    /// <summary>What the tool does, as shown to the model.</summary>
    public required string Description { get; init; }
}
