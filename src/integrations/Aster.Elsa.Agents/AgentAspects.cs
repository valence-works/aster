using System.Text.Json;
using System.Text.Json.Serialization;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Elsa.Agents.Persistence.Entities;
using AgentAspect = Aster.Elsa.Agents.Aspects.Agent;
using AgentConfigAspect = Aster.Elsa.Agents.Aspects.AgentConfig;
using ElsaAgentConfig = Elsa.Agents.AgentConfig;

namespace Aster.Elsa.Agents;

/// <summary>
/// Maps Elsa's <see cref="AgentDefinition"/> to the two aspects of an <c>ElsaAgent</c> resource and back. Agent
/// references (<c>AgentConfig.Agents</c>) are passed through as given; <see cref="AsterAgentStore"/> translates them.
/// </summary>
internal static class AgentAspects
{
    // Property names as declared, so stored keys match the facet IDs derived from the aspect types. Null properties are
    // left out, so a missing value reads back as null and never fails an optional facet's type check.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Reads accept any casing, as Aster's own binder does.
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The aspect payloads of <paramref name="entity"/>, with <paramref name="agents"/> as the agent references.</summary>
    public static Dictionary<string, object> ToAspects(AgentDefinition entity, string[]? agents)
    {
        var config = entity.AgentConfig;
        var agent = new AgentAspect { Name = entity.Name, Description = entity.Description };
        var configAspect = new AgentConfigAspect
        {
            Name = config.Name,
            FunctionName = config.FunctionName,
            Description = config.Description,
            PromptTemplate = config.PromptTemplate,
            InputVariables = config.InputVariables?.ToList(),
            OutputVariable = config.OutputVariable,
            ExecutionSettings = config.ExecutionSettings,
            Skills = config.Skills?.ToArray(),
            Agents = agents,
        };

        // Snapshots as JSON elements: immutable, so a caller that keeps changing the entity can't change a stored
        // version (the in-memory provider keeps aspect objects as given), and stored the same way by every provider.
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AsterAgentResource.AgentAspectKey] = JsonSerializer.SerializeToElement(agent, JsonOptions),
            [AsterAgentResource.ConfigAspectKey] = JsonSerializer.SerializeToElement(configAspect, JsonOptions),
        };
    }

    /// <summary>Decodes both aspects of a stored version once.</summary>
    public static StoredAgent Decode(Resource resource, ITypedAspectBinder binder) => new(
        resource,
        Read<AgentAspect>(resource, AsterAgentResource.AgentAspectKey, binder) ?? new AgentAspect(),
        Read<AgentConfigAspect>(resource, AsterAgentResource.ConfigAspectKey, binder) ?? new AgentConfigAspect());

    /// <summary>The agent's name in a stored version.</summary>
    public static string? ReadName(Resource resource, ITypedAspectBinder binder) =>
        Read<AgentAspect>(resource, AsterAgentResource.AgentAspectKey, binder)?.Name;

    /// <summary>The Elsa entity for a stored version, with each agent reference mapped through <paramref name="agentName"/>.</summary>
    public static AgentDefinition ToEntity(StoredAgent stored, string? elsaTenantId, Func<string, string> agentName)
    {
        var (resource, agent, config) = stored;
        return new AgentDefinition
        {
            Id = resource.ResourceId,
            TenantId = elsaTenantId,
            Name = agent.Name!,
            Description = agent.Description!,
            AgentConfig = new ElsaAgentConfig
            {
                Name = config.Name!,
                FunctionName = config.FunctionName,
                Description = config.Description!,
                PromptTemplate = config.PromptTemplate!,
                InputVariables = config.InputVariables!,
                OutputVariable = config.OutputVariable!,
                ExecutionSettings = config.ExecutionSettings!,
                Skills = config.Skills?.ToList()!,
                Agents = config.Agents?.Select(agentName).ToList()!,
            },
        };
    }

    // Stored aspects come back as JSON elements from every provider, so read them directly; anything else goes
    // through Aster's binder.
    private static T? Read<T>(Resource resource, string aspectKey, ITypedAspectBinder binder) where T : class =>
        resource.Aspects.TryGetValue(aspectKey, out var raw)
            ? raw is JsonElement element ? element.Deserialize<T>(ReadOptions) : binder.Deserialize<T>(raw)
            : null;
}

/// <summary>A stored agent version with its aspects decoded.</summary>
internal sealed record StoredAgent(Resource Resource, AgentAspect Agent, AgentConfigAspect Config);
