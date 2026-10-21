namespace Aster.Elsa.Agents;

/// <summary>The outcome of the import for one source agent.</summary>
/// <param name="AgentId">The agent ID, which is also its Aster resource ID.</param>
/// <param name="Name">The agent name.</param>
/// <param name="Status">What happened to the agent.</param>
/// <param name="Error">Why the import failed, when <paramref name="Status"/> is <see cref="AsterAgentImportStatus.Failed"/>.</param>
public sealed record AsterAgentImportItem(string AgentId, string? Name, AsterAgentImportStatus Status, string? Error = null);
