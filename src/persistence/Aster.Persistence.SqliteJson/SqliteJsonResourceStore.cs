using Aster.Persistence.Relational;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// SQLite JSON-backed implementation of Aster's low-level definition and resource version contracts.
/// </summary>
internal sealed class SqliteJsonResourceStore : RelationalJsonResourceStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonResourceStore"/>, creating the schema when
    /// <see cref="SqliteJsonAsterOptions.InitializeSchema"/> is set (idempotent).
    /// </summary>
    /// <param name="options">Provider options.</param>
    public SqliteJsonResourceStore(SqliteJsonAsterOptions options)
        : base(CreateDialect(options))
    {
        if (options.InitializeSchema)
            SqliteJsonSchema.Initialize(options.ConnectionString);
    }

    internal static SqliteJsonDialect CreateDialect(SqliteJsonAsterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        return new SqliteJsonDialect(options.ConnectionString);
    }
}
