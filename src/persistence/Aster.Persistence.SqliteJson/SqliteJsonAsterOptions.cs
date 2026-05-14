namespace Aster.Persistence.SqliteJson;

/// <summary>
/// Options for the SQLite JSON persistence provider.
/// </summary>
public sealed class SqliteJsonAsterOptions
{
    /// <summary>
    /// SQLite connection string used by the provider.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Whether the provider should create required tables and indexes when constructed.
    /// </summary>
    public bool InitializeSchema { get; init; } = true;
}
