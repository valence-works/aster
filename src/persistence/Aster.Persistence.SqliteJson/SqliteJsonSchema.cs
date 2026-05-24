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
                tenant_id TEXT NOT NULL DEFAULT 'default',
                definition_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                id TEXT NOT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (tenant_id, definition_id, version)
            );

            CREATE TABLE IF NOT EXISTS resource_versions (
                tenant_id TEXT NOT NULL DEFAULT 'default',
                resource_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                id TEXT NOT NULL,
                definition_id TEXT NOT NULL,
                definition_version INTEGER NULL,
                created TEXT NOT NULL,
                owner TEXT NULL,
                hash TEXT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (tenant_id, resource_id, version)
            );

            CREATE TABLE IF NOT EXISTS activation_states (
                tenant_id TEXT NOT NULL DEFAULT 'default',
                resource_id TEXT NOT NULL,
                channel TEXT NOT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (tenant_id, resource_id, channel)
            );
            """;

        command.ExecuteNonQuery();

        AddTenantColumnIfMissing(connection, "resource_definitions");
        AddTenantColumnIfMissing(connection, "resource_versions");
        AddTenantColumnIfMissing(connection, "activation_states");

        using var index = connection.CreateCommand();
        index.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_resource_versions_definition_id
                ON resource_versions (tenant_id, definition_id);
            """;
        index.ExecuteNonQuery();
    }

    private static void AddTenantColumnIfMissing(SqliteConnection connection, string tableName)
    {
        using var tableInfo = connection.CreateCommand();
        tableInfo.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = tableInfo.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "tenant_id", StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN tenant_id TEXT NOT NULL DEFAULT 'default';";
        alter.ExecuteNonQuery();
    }
}
