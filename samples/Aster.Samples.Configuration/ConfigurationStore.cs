using Microsoft.Extensions.DependencyInjection;
using Aster.Core.Extensions;
using Aster.Persistence.SqliteJson;
using Aster.Samples.Configuration.ChannelConfiguration;

namespace Aster.Samples.Configuration;

/// <summary>
/// Builds the Aster container that stores the configuration: in memory by default, or in a SQLite
/// file when a path is given.
/// </summary>
public static class ConfigurationStore
{
    /// <summary>Builds the configuration store.</summary>
    /// <param name="sqliteDatabasePath">A SQLite database file to use instead of the in-memory provider.</param>
    public static ServiceProvider Build(string? sqliteDatabasePath = null)
    {
        var services = new ServiceCollection()
            .AddAsterCore()
            .AddChannelConfigurationStore();

        if (sqliteDatabasePath is not null)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={sqliteDatabasePath}");

        return services.BuildServiceProvider();
    }
}
