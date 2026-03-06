namespace Aster.Persistence.Sqlite;

/// <summary>
/// Configuration options for the Sqlite persistence provider.
/// </summary>
public sealed class SqlitePersistenceOptions
{
    /// <summary>
    /// Sqlite connection string. Defaults to an in-memory database for development.
    /// </summary>
    /// <example><c>Data Source=aster.db</c></example>
    public string ConnectionString { get; set; } = "Data Source=:memory:";

    /// <summary>
    /// Threshold in milliseconds above which a query is logged as slow (<c>Warning</c> level).
    /// Default: 500 ms.
    /// </summary>
    public int SlowQueryThresholdMs { get; set; } = 500;
}
