using Aster.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// Extension methods for registering the PostgreSQL JSONB provider.
/// </summary>
public static class PostgresJsonbAsterServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL JSONB storage for definitions, shared aspects, resource versions, activation state,
    /// lifecycle markers, version pruning and portability. Call it after <c>AddAsterCore()</c>.
    /// </summary>
    /// <remarks>
    /// Queries still run through the core's in-memory query service over this provider's reads until the
    /// Postgres query translator lands (#98).
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the connection string and schema bootstrap.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAsterPostgresJsonb(
        this IServiceCollection services,
        Action<PostgresJsonbAsterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PostgresJsonbAsterOptions();
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString, nameof(PostgresJsonbAsterOptions.ConnectionString));

        services.AddSingleton(options);
        services.AddSingleton<PostgresJsonbDataSource>();
        services.AddSingleton<PostgresJsonbResourceStore>();
        services.AddSingleton<PostgresJsonbAspectDefinitionStore>();
        services.AddSingleton<IAspectDefinitionStore>(sp => sp.GetRequiredService<PostgresJsonbAspectDefinitionStore>());
        services.AddSingleton<IResourceDefinitionStore>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourceVersionReader>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourceVersionWriter>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourceActivationStateReader>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourceVersionPruningStore>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourcePortabilityStore>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourceLifecycleMarkerStore>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());
        services.AddSingleton<IResourceLifecycleMarkerClearStore>(sp => sp.GetRequiredService<PostgresJsonbResourceStore>());

        return services;
    }
}
