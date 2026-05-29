using Aster.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// DI extensions for the SQLite JSON persistence provider.
/// </summary>
public static class SqliteJsonAsterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite JSON resource version and definition store primitives.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">Options configuration callback.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddAsterSqliteJson(
        this IServiceCollection services,
        Action<SqliteJsonAsterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqliteJsonAsterOptions { ConnectionString = "Data Source=aster.db" };
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<SqliteJsonResourceStore>();
        services.AddSingleton<SqliteJsonQueryService>();
        services.AddSingleton<SqliteJsonQueryProviderIdentity>();
        services.AddSingleton<SqliteJsonQueryCapabilitiesProvider>();
        services.AddSingleton<IResourceDefinitionStore>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourceVersionReader>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourceVersionWriter>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourceVersionPruningStore>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourcePortabilityStore>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourceLifecycleMarkerStore>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourceLifecycleMarkerClearStore>(sp => sp.GetRequiredService<SqliteJsonResourceStore>());
        services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<SqliteJsonQueryService>());
        services.AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<SqliteJsonQueryProviderIdentity>());
        services.AddSingleton<IResourceQueryCapabilitiesProvider>(sp => sp.GetRequiredService<SqliteJsonQueryCapabilitiesProvider>());

        return services;
    }
}
