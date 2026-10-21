using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Agents;

/// <summary>
/// Selects the Aster agent store in Elsa's agent persistence feature.
/// </summary>
public static class AsterAgentPersistenceFeatureExtensions
{
    /// <summary>
    /// Registers the Aster agent store (<see cref="AsterAgentsServiceCollectionExtensions.AddAsterElsaAgents"/>) and passes
    /// it to <see cref="AgentPersistenceFeature.UseAgentStore"/>, so Elsa's <see cref="IAgentStore"/> is Aster-backed.
    /// Leaving this call out keeps Elsa's own store. Don't also call Elsa's <c>UseEntityFrameworkCore</c> on the same
    /// feature: its feature selects Elsa's EF Core store again while Elsa applies features.
    /// </summary>
    /// <param name="feature">Elsa's agent persistence feature, as configured in <c>UseAgentPersistence</c>.</param>
    /// <param name="configure">Configures the store's channel and activation.</param>
    /// <returns>The <paramref name="feature"/> for chaining.</returns>
    public static AgentPersistenceFeature UseAsterAgentStore(this AgentPersistenceFeature feature, Action<AsterAgentStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        feature.Services.AddAsterElsaAgents(configure);
        return feature.UseAgentStore(static services => services.GetRequiredService<AsterAgentStore>());
    }
}
