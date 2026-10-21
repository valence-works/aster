using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;
using AgentAspect = Aster.Elsa.Agents.Aspects.Agent;
using AgentConfigAspect = Aster.Elsa.Agents.Aspects.AgentConfig;

namespace Aster.Elsa.Agents;

/// <summary>
/// The Aster resource definition that holds Elsa agent definitions, for host code that works on agents through Aster
/// directly: queries, promotion between channels, rollback, history and export. The store registers it in each tenant
/// on its first write there.
/// </summary>
/// <remarks>
/// An agent is one resource whose <c>ResourceId</c> is the Elsa agent ID, with two aspects:
/// <list type="bullet">
/// <item><see cref="AgentAspectKey"/>: the facets <c>Name</c> and <c>Description</c> of Elsa's <c>AgentDefinition</c>.</item>
/// <item><see cref="ConfigAspectKey"/>: Elsa's <c>AgentConfig</c>, with the facets <c>Name</c>, <c>FunctionName</c>,
/// <c>Description</c>, <c>PromptTemplate</c>, <c>Skills</c> (<c>string[]</c>), <c>Agents</c> (<c>reference[]</c> to other
/// agents, by resource ID), and the <c>object</c> facets <c>OutputVariable</c> and <c>ExecutionSettings</c>.
/// <c>InputVariables</c> is stored with the aspect but is not a facet.</item>
/// </list>
/// </remarks>
public static class AsterAgentResource
{
    /// <summary>The definition ID of agent resources: <c>ElsaAgent</c>.</summary>
    public const string DefinitionId = "ElsaAgent";

    /// <summary>The aspect key of the agent's name and description: <c>Agent</c>.</summary>
    public const string AgentAspectKey = "Agent";

    /// <summary>The aspect key of the agent's configuration: <c>AgentConfig</c>.</summary>
    public const string ConfigAspectKey = "AgentConfig";

    /// <summary>Creates the definition the store registers, in the default tenant.</summary>
    /// <returns>The <c>ElsaAgent</c> resource definition.</returns>
    public static ResourceDefinition CreateDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(DefinitionId)
        .WithAspect<AgentAspect>()
        .WithAspect<AgentConfigAspect>()
        .Build();
}
