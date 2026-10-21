namespace Aster.Elsa.Agents.Aspects;

/// <summary>
/// The <c>Agent</c> aspect: the name and description of Elsa's <c>AgentDefinition</c>. The type name is the aspect key
/// (<see cref="AsterAgentResource.AgentAspectKey"/>), and each property is a <c>string</c> facet.
/// </summary>
internal sealed record Agent
{
    /// <summary>The agent's name; Elsa resolves agents by it.</summary>
    public string? Name { get; init; }

    /// <summary>The agent's description.</summary>
    public string? Description { get; init; }
}
