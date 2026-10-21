using Aster.Core.Abstractions;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.Entities;
using ElsaAgentConfig = Elsa.Agents.AgentConfig;

namespace Aster.Elsa.Agents;

/// <summary>
/// Imports agents from another <see cref="IAgentStore"/> through <see cref="AsterAgentStore"/>, so imported agents are
/// stored exactly as the store saves them: references translated, definition registered, version activated.
/// </summary>
internal sealed class AsterAgentImporter(AsterAgentStore target, IResourceManager resources) : IAsterAgentImporter
{
    /// <inheritdoc />
    public async Task<AsterAgentImportResult> ImportAsync(IAgentStore source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is AsterAgentStore)
            throw new ArgumentException("The source is the Aster agent store itself. Import from the store the agents were kept in before, such as Elsa's EF Core store.", nameof(source));

        var tenant = target.CurrentTenant();
        var items = new Dictionary<string, AsterAgentImportItem>(StringComparer.Ordinal);
        var pending = new List<AgentDefinition>();

        foreach (var agent in (await source.ListAsync(cancellationToken)).OrderBy(static agent => agent.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(agent.Id))
                items[$"#{items.Count}"] = new AsterAgentImportItem(agent.Id ?? "", agent.Name, AsterAgentImportStatus.Failed, "The agent has no ID.");
            else if (items.ContainsKey(agent.Id))
                continue;
            else if (await resources.GetLatestVersionAsync(agent.Id, tenant, cancellationToken) is not null)
                items[agent.Id] = new AsterAgentImportItem(agent.Id, agent.Name, AsterAgentImportStatus.Skipped);
            else
                pending.Add(agent);
        }

        // Import agents after the agents they reference, so each name resolves to an imported agent's ID.
        for (var progress = true; progress && pending.Count > 0;)
        {
            progress = false;
            foreach (var agent in pending.ToList())
            {
                if (WaitsOnPending(agent, pending))
                    continue;

                items[agent.Id] = await ImportAsync(agent, agent.AgentConfig?.Agents?.ToList(), cancellationToken);
                pending.Remove(agent);
                progress = true;
            }
        }

        // What remains references itself or sits in a reference cycle: save it without the references into the
        // remaining agents first, then save every reference once all of them exist.
        var cycle = pending.ToList();
        var pendingNames = cycle.Select(static agent => agent.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var agent in cycle)
        {
            var outside = agent.AgentConfig?.Agents?.Where(name => !pendingNames.Contains(name)).ToList();
            items[agent.Id] = await ImportAsync(agent, outside, cancellationToken);
        }

        foreach (var agent in cycle.Where(agent => items[agent.Id].Status == AsterAgentImportStatus.Imported))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await target.SaveAsync(Copy(agent, agent.AgentConfig?.Agents?.ToList()), activate: true, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                items[agent.Id] = Failed(agent, $"Version 1 was imported without its agent references, but saving them failed: {exception.Message}");
            }
        }

        return new AsterAgentImportResult { Items = items.Values.OrderBy(static item => item.AgentId, StringComparer.Ordinal).ToList() };
    }

    private static bool WaitsOnPending(AgentDefinition agent, List<AgentDefinition> pending) =>
        agent.AgentConfig?.Agents is { Count: > 0 } references
        && pending.Any(other => references.Contains(other.Name, StringComparer.Ordinal));

    private async Task<AsterAgentImportItem> ImportAsync(AgentDefinition agent, List<string>? agentReferences, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Imported agents were live in Elsa, so they are activated whatever AutoActivate says.
            await target.SaveNewAsync(Copy(agent, agentReferences), activate: true, cancellationToken);
            return new AsterAgentImportItem(agent.Id, agent.Name, AsterAgentImportStatus.Imported);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed(agent, exception.Message);
        }
    }

    private static AsterAgentImportItem Failed(AgentDefinition agent, string error) =>
        new(agent.Id, agent.Name, AsterAgentImportStatus.Failed, error);

    /// <summary>A copy to save, so the source store's entity is never changed.</summary>
    private static AgentDefinition Copy(AgentDefinition agent, List<string>? agentReferences)
    {
        var config = agent.AgentConfig;
        return new AgentDefinition
        {
            Id = agent.Id,
            TenantId = agent.TenantId,
            Name = agent.Name,
            Description = agent.Description,
            AgentConfig = config is null
                ? null!
                : new ElsaAgentConfig
                {
                    Name = config.Name,
                    FunctionName = config.FunctionName,
                    Description = config.Description,
                    PromptTemplate = config.PromptTemplate,
                    InputVariables = config.InputVariables,
                    OutputVariable = config.OutputVariable,
                    ExecutionSettings = config.ExecutionSettings,
                    Skills = config.Skills,
                    Agents = agentReferences!,
                },
        };
    }
}
