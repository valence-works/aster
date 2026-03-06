using Aster.Core.Abstractions;
using Aster.Persistence.Sqlite.Persistence;
using Aster.Persistence.Sqlite.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Persistence.Sqlite.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register the Sqlite persistence provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Aster Sqlite persistence provider, including
    /// <see cref="IResourceDefinitionStore"/>, <see cref="IResourceWriteStore"/>,
    /// and <see cref="IResourceQueryService"/> implementations backed by Sqlite.
    /// Runs schema initialization synchronously on registration.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Action to configure <see cref="SqlitePersistenceOptions"/>.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        Action<SqlitePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqlitePersistenceOptions();
        configure(options);

        // Run schema initialization eagerly
        var schemaInitializer = new SchemaInitializer(options, NullLogger<SchemaInitializer>.Instance);
        schemaInitializer.EnsureCreated();

        services.AddSingleton(options);

        // Definition store
        services.AddSingleton<SqliteResourceDefinitionStore>();
        services.AddSingleton<IResourceDefinitionStore>(sp => sp.GetRequiredService<SqliteResourceDefinitionStore>());

        // Write store
        services.AddSingleton<SqliteResourceWriteStore>();
        services.AddSingleton<IResourceWriteStore>(sp => sp.GetRequiredService<SqliteResourceWriteStore>());

        // Query service
        services.AddSingleton<SqliteResourceQueryService>();
        services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<SqliteResourceQueryService>());

        return services;
    }
}
