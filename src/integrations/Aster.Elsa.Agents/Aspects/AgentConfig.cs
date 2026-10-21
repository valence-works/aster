using Aster.Core.Definitions;
using Elsa.Agents;

namespace Aster.Elsa.Agents.Aspects;

/// <summary>
/// The <c>AgentConfig</c> aspect: Elsa's <c>AgentConfig</c>, property for property. The type name is the aspect key
/// (<see cref="AsterAgentResource.ConfigAspectKey"/>). Facets are derived from the properties: strings, the
/// <c>string[]</c> facet <see cref="Skills"/>, the <c>reference[]</c> facet <see cref="Agents"/>, and the <c>object</c>
/// facets <see cref="OutputVariable"/> and <see cref="ExecutionSettings"/>. <see cref="InputVariables"/> is a collection
/// of objects, which is not a facet type, so it is stored without one.
/// </summary>
internal sealed record AgentConfig
{
    public string? Name { get; init; }

    public string? FunctionName { get; init; }

    public string? Description { get; init; }

    public string? PromptTemplate { get; init; }

    public List<InputVariableConfig>? InputVariables { get; init; }

    public OutputVariableConfig? OutputVariable { get; init; }

    public ExecutionSettingsConfig? ExecutionSettings { get; init; }

    /// <summary>Skill names. Skills are registered in code (Elsa's skill providers), not stored, so this is <c>string[]</c>.</summary>
    public string[]? Skills { get; init; }

    /// <summary>
    /// The resource IDs of the agents this agent works with. Elsa names them by agent name; the store translates names
    /// to resource IDs on save and back on read. A name that matches no agent is stored as written.
    /// </summary>
    [ReferenceFacet(AsterAgentResource.DefinitionId)]
    public string[]? Agents { get; init; }
}
