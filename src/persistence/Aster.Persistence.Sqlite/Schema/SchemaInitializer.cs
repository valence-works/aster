using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aster.Persistence.Sqlite.Schema;

/// <summary>
/// Creates the Aster Sqlite schema on first run. Ships a single fixed schema version;
/// in-place migrations are out of Phase 2 scope.
/// </summary>
internal sealed partial class SchemaInitializer
{
    private readonly SqlitePersistenceOptions options;
    private readonly ILogger<SchemaInitializer> logger;

    public SchemaInitializer(SqlitePersistenceOptions options, ILogger<SchemaInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.logger = logger;
    }

    /// <summary>
    /// Ensures the schema tables exist. Idempotent — safe to call on every startup.
    /// </summary>
    public void EnsureCreated()
    {
        using var connection = new SqliteConnection(options.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();

        LogSchemaInitialized(options.ConnectionString);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS ResourceDefinitionRecord (
            DefinitionId    TEXT    NOT NULL,
            Version         INTEGER NOT NULL,
            VersionId       TEXT    NOT NULL,
            IsSingleton     INTEGER NOT NULL DEFAULT 0,
            PayloadJson     TEXT    NOT NULL,
            CreatedUtc      TEXT    NOT NULL,
            PRIMARY KEY (DefinitionId, Version),
            UNIQUE (VersionId)
        );

        CREATE TABLE IF NOT EXISTS ResourceRecord (
            ResourceId        TEXT    NOT NULL,
            Version           INTEGER NOT NULL,
            VersionId         TEXT    NOT NULL,
            DefinitionId      TEXT    NOT NULL,
            DefinitionVersion INTEGER,
            AspectsJson       TEXT    NOT NULL,
            CreatedUtc        TEXT    NOT NULL,
            Owner             TEXT,
            Hash              TEXT,
            PRIMARY KEY (ResourceId, Version),
            UNIQUE (VersionId)
        );

        CREATE TABLE IF NOT EXISTS ActivationRecord (
            ResourceId          TEXT    NOT NULL,
            Channel             TEXT    NOT NULL,
            Mode                TEXT    NOT NULL,
            ActiveVersionsJson  TEXT    NOT NULL,
            LastUpdatedUtc      TEXT    NOT NULL,
            PRIMARY KEY (ResourceId, Channel)
        );

        CREATE INDEX IF NOT EXISTS IX_ResourceRecord_DefinitionId
            ON ResourceRecord (DefinitionId);
        """;

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Sqlite schema initialized for connection '{ConnectionString}'.")]
    private partial void LogSchemaInitialized(string connectionString);
}
