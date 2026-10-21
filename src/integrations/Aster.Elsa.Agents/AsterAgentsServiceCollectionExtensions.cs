using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aster.Elsa.Agents;

/// <summary>
/// Registers the Aster agent store's services.
/// </summary>
public static class AsterAgentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Aster agent store and <see cref="IAsterAgentImporter"/>, without selecting the store in Elsa:
    /// <see cref="AsterAgentPersistenceFeatureExtensions.UseAsterAgentStore"/> does both. Call it on its own in a host that
    /// keeps Elsa's agent store and only imports from it. Aster itself (<c>AddAsterCore()</c> and a provider) is the
    /// host's to register.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the store's channel and activation.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAsterElsaAgents(this IServiceCollection services, Action<AsterAgentStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<AsterAgentStoreOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AsterAgentStoreOptions>, AsterAgentStoreOptionsValidator>());
        services.TryAddSingleton<AgentDefinitionRegistrar>();
        services.TryAddScoped<AsterAgentStore>();
        services.TryAddScoped<IAsterAgentImporter, AsterAgentImporter>();
        return services;
    }
}
