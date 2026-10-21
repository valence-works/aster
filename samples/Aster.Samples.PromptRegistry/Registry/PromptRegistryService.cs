using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Samples.PromptRegistry.Model;

namespace Aster.Samples.PromptRegistry.Registry;

/// <summary>
/// The registry's host API over <see cref="IResourceManager"/>: save versions, move them through
/// <c>candidate</c> and <c>production</c>, run A/B tests with two active versions in
/// <c>production</c>, and resolve what an agent runs with in a channel.
/// </summary>
public sealed class PromptRegistryService(IResourceManager resources, ITypedAspectBinder binder)
{
    /// <summary>Saves a prompt version: version 1 on first save, a new version after that. The version is a draft until released.</summary>
    public Task<Resource> SavePromptAsync(string resourceId, Prompt prompt, CancellationToken cancellationToken = default) =>
        SaveAsync(RegistryDefinitions.PromptId, resourceId, prompt, cancellationToken);

    /// <summary>Saves a tool version.</summary>
    public Task<Resource> SaveToolAsync(string resourceId, Tool tool, CancellationToken cancellationToken = default) =>
        SaveAsync(RegistryDefinitions.ToolId, resourceId, tool, cancellationToken);

    /// <summary>
    /// Saves an agent version. Aster rejects the save when <see cref="Agent.Prompt"/> or a tool ID
    /// does not name an existing prompt or tool.
    /// </summary>
    public Task<Resource> SaveAgentAsync(string resourceId, Agent agent, CancellationToken cancellationToken = default) =>
        SaveAsync(RegistryDefinitions.AgentId, resourceId, agent, cancellationToken);

    private async Task<Resource> SaveAsync(string definitionId, string resourceId, object aspect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(aspect);
        var latest = await resources.GetLatestVersionAsync(resourceId, cancellationToken);
        if (latest is null)
        {
            return await resources.CreateAsync(definitionId, new CreateResourceRequest
            {
                ResourceId = resourceId,
                InitialAspects = new() { [definitionId] = aspect },
            }, cancellationToken);
        }

        return await resources.UpdateAsync(resourceId, new UpdateResourceRequest
        {
            BaseVersion = latest.Version,
            AspectUpdates = new() { [definitionId] = aspect },
        }, cancellationToken);
    }

    /// <summary>Makes <paramref name="version"/> the version under evaluation in <c>candidate</c>.</summary>
    public async Task ReleaseToCandidateAsync(string resourceId, int version, CancellationToken cancellationToken = default) =>
        await resources.ActivateAsync(resourceId, version, Channels.Candidate, allowMultipleActive: false, cancellationToken);

    /// <summary>
    /// Promotes the candidate version to <c>production</c> as its only active version, replacing
    /// whatever was active there, including both arms of an A/B test.
    /// </summary>
    /// <returns>The promoted version.</returns>
    /// <exception cref="InvalidOperationException">No version is active in <c>candidate</c>.</exception>
    public async Task<int> PromoteToProductionAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var candidate = await GetCandidateVersionAsync(resourceId, cancellationToken);
        await resources.ActivateAsync(resourceId, candidate, Channels.Production, allowMultipleActive: false, cancellationToken);
        return candidate;
    }

    /// <summary>
    /// Starts an A/B test: activates the candidate version in <c>production</c> next to the version
    /// already active there (<c>allowMultipleActive: true</c>), so traffic splits between them.
    /// </summary>
    /// <returns>The versions now active in <c>production</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Nothing is active in <c>production</c> to compare against, or the candidate is already active there.
    /// </exception>
    public async Task<IReadOnlyList<int>> StartAbTestAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var candidate = await GetCandidateVersionAsync(resourceId, cancellationToken);
        var production = await GetActiveVersionsAsync(resourceId, Channels.Production, cancellationToken);
        if (production.Count == 0)
            throw new InvalidOperationException($"Nothing of '{resourceId}' is active in production to test v{candidate} against; promote it instead.");
        if (production.Contains(candidate))
            throw new InvalidOperationException($"Version {candidate} of '{resourceId}' is already active in production.");

        await resources.ActivateAsync(resourceId, candidate, Channels.Production, allowMultipleActive: true, cancellationToken);
        return await GetActiveVersionsAsync(resourceId, Channels.Production, cancellationToken);
    }

    /// <summary>
    /// Ends an A/B test: <paramref name="winner"/> stays as the only version active in
    /// <c>production</c> and the other arms are deactivated.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="winner"/> is not active in <c>production</c>.</exception>
    public async Task ConcludeAbTestAsync(string resourceId, int winner, CancellationToken cancellationToken = default)
    {
        var production = await GetActiveVersionsAsync(resourceId, Channels.Production, cancellationToken);
        if (!production.Contains(winner))
            throw new InvalidOperationException($"Version {winner} of '{resourceId}' is not active in production.");

        foreach (var loser in production.Where(version => version != winner))
            await resources.DeactivateAsync(resourceId, loser, Channels.Production, cancellationToken);
    }

    /// <summary>Returns the versions active in <paramref name="channel"/>, in ascending order.</summary>
    public async Task<IReadOnlyList<int>> GetActiveVersionsAsync(string resourceId, string channel, CancellationToken cancellationToken = default) =>
        (await resources.GetActiveVersionsAsync(resourceId, channel, cancellationToken))
            .Select(static resource => resource.Version)
            .Order()
            .ToList();

    /// <summary>
    /// Resolves what <paramref name="agentId"/> runs with in <paramref name="channel"/>: the agent,
    /// its prompt and its tools, each at the version active in that channel. Where two versions
    /// are active (an A/B test), <paramref name="subjectId"/> picks one, the same one every time.
    /// </summary>
    /// <exception cref="InvalidOperationException">The agent or something it references is not active in <paramref name="channel"/>.</exception>
    public async Task<ResolvedAgent> ResolveAsync(string agentId, string channel, string subjectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        var agent = await SelectAsync(agentId, channel, subjectId, cancellationToken);
        var agentAspect = agent.GetAspect<Agent>(RegistryDefinitions.AgentId, binder)!;
        var prompt = await SelectAsync(agentAspect.Prompt, channel, subjectId, cancellationToken);

        var tools = new List<ResolvedTool>();
        foreach (var toolId in agentAspect.Tools)
        {
            var tool = await SelectAsync(toolId, channel, subjectId, cancellationToken);
            tools.Add(new ResolvedTool(toolId, tool.Version, tool.GetAspect<Tool>(RegistryDefinitions.ToolId, binder)!));
        }

        return new ResolvedAgent(
            agentId,
            agent.Version,
            agentAspect,
            agentAspect.Prompt,
            prompt.Version,
            prompt.GetAspect<Prompt>(RegistryDefinitions.PromptId, binder)!,
            tools);
    }

    /// <summary>Resolves a prompt on its own, as <see cref="ResolveAsync"/> resolves an agent's prompt.</summary>
    /// <exception cref="InvalidOperationException">The prompt is not active in <paramref name="channel"/>.</exception>
    public async Task<Prompt> ResolvePromptAsync(string promptId, string channel, string subjectId, CancellationToken cancellationToken = default) =>
        (await SelectAsync(promptId, channel, subjectId, cancellationToken)).GetAspect<Prompt>(RegistryDefinitions.PromptId, binder)!;

    private async Task<Resource> SelectAsync(string resourceId, string channel, string subjectId, CancellationToken cancellationToken)
    {
        var active = (await resources.GetActiveVersionsAsync(resourceId, channel, cancellationToken))
            .OrderBy(static resource => resource.Version)
            .ToList();

        return active.Count switch
        {
            0 => throw new InvalidOperationException($"'{resourceId}' has no version active in '{channel}'."),
            1 => active[0],
            _ => active[VariantSelector.Select(resourceId, subjectId, active.Count)],
        };
    }

    private async Task<int> GetCandidateVersionAsync(string resourceId, CancellationToken cancellationToken) =>
        (await GetActiveVersionsAsync(resourceId, Channels.Candidate, cancellationToken)) is [var version]
            ? version
            : throw new InvalidOperationException($"'{resourceId}' has no version in candidate.");
}
