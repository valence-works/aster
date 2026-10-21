using Elsa.Agents.Persistence.Contracts;

namespace Aster.Elsa.Agents;

/// <summary>
/// The one-time import of agent definitions from another <see cref="IAgentStore"/>, such as Elsa's EF Core or in-memory
/// store, into the Aster agent store.
/// </summary>
public interface IAsterAgentImporter
{
    /// <summary>
    /// Imports every agent that <paramref name="source"/> lists in Elsa's current tenant into the mapped Aster tenant,
    /// keeping its ID, and activates it in the store's channel. The import is idempotent: an agent whose ID already exists
    /// in Aster, in any lifecycle state, is skipped and left unchanged, so running it again imports only what is new.
    /// </summary>
    /// <remarks>
    /// Agents are imported after the agents they reference by name, so every reference resolves to a resource ID. Agents
    /// that reference each other in a cycle (or themselves), and agents that depend on such a cycle, are saved twice:
    /// version 1 without the references into the agents not yet imported, then version 2 with all of them. A failure is
    /// reported for that agent and the import goes on.
    /// </remarks>
    /// <param name="source">The store to import from; not the Aster store itself.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One item per source agent, in ID order.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is the Aster agent store.</exception>
    /// <exception cref="Aster.Core.Exceptions.TenantScopeException">Elsa's current tenant has no Aster tenant scope.</exception>
    Task<AsterAgentImportResult> ImportAsync(IAgentStore source, CancellationToken cancellationToken = default);
}
