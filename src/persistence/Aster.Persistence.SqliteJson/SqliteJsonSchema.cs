using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

internal static class SqliteJsonSchema
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS resource_definitions (
                definition_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                id TEXT NOT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (definition_id, version)
            );

            CREATE TABLE IF NOT EXISTS resource_versions (
                resource_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                id TEXT NOT NULL,
                definition_id TEXT NOT NULL,
                definition_version INTEGER NULL,
                created TEXT NOT NULL,
                owner TEXT NULL,
                hash TEXT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (resource_id, version)
            );

            CREATE INDEX IF NOT EXISTS ix_resource_versions_definition_id
                ON resource_versions (definition_id);

            CREATE TABLE IF NOT EXISTS activation_states (
                resource_id TEXT NOT NULL,
                channel TEXT NOT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (resource_id, channel)
            );
            """;

        command.ExecuteNonQuery();
    }
}
