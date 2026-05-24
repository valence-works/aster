using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

internal static class SqliteJsonSchema
{
    private const string CreateResourceDefinitionsSql = """
        CREATE TABLE IF NOT EXISTS resource_definitions (
            tenant_id TEXT NOT NULL DEFAULT 'default',
            definition_id TEXT NOT NULL,
            version INTEGER NOT NULL,
            id TEXT NOT NULL,
            payload TEXT NOT NULL,
            PRIMARY KEY (tenant_id, definition_id, version)
        );
        """;

    private const string CreateResourceVersionsSql = """
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
        """;

    private const string CreateActivationStatesSql = """
        CREATE TABLE IF NOT EXISTS activation_states (
            tenant_id TEXT NOT NULL DEFAULT 'default',
            resource_id TEXT NOT NULL,
            channel TEXT NOT NULL,
            payload TEXT NOT NULL,
            PRIMARY KEY (tenant_id, resource_id, channel)
        );
        """;

    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            """;

        command.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();
        EnsureTenantAwareTable(
            connection,
            transaction,
            "resource_definitions",
            CreateResourceDefinitionsSql,
            ["tenant_id", "definition_id", "version", "id", "payload"],
            ["tenant_id", "definition_id", "version"]);
        EnsureTenantAwareTable(
            connection,
            transaction,
            "resource_versions",
            CreateResourceVersionsSql,
            ["tenant_id", "resource_id", "version", "id", "definition_id", "definition_version", "created", "owner", "hash", "payload"],
            ["tenant_id", "resource_id", "version"]);
        EnsureTenantAwareTable(
            connection,
            transaction,
            "activation_states",
            CreateActivationStatesSql,
            ["tenant_id", "resource_id", "channel", "payload"],
            ["tenant_id", "resource_id", "channel"]);

        using var index = connection.CreateCommand();
        index.Transaction = transaction;
        index.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_resource_versions_definition_id
                ON resource_versions (tenant_id, definition_id);
            """;
        index.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void EnsureTenantAwareTable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string createSql,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> expectedPrimaryKey)
    {
        ExecuteNonQuery(connection, transaction, createSql);

        var tableColumns = ReadColumns(connection, transaction, tableName);
        var actualPrimaryKey = tableColumns
            .Where(static column => column.PrimaryKeyOrdinal > 0)
            .OrderBy(static column => column.PrimaryKeyOrdinal)
            .Select(static column => column.Name)
            .ToArray();

        if (actualPrimaryKey.SequenceEqual(expectedPrimaryKey, StringComparer.OrdinalIgnoreCase))
            return;

        var legacyTableName = $"{tableName}__legacy_tenant_bootstrap";
        ExecuteNonQuery(connection, transaction, $"DROP TABLE IF EXISTS {legacyTableName};");
        ExecuteNonQuery(connection, transaction, $"ALTER TABLE {tableName} RENAME TO {legacyTableName};");
        ExecuteNonQuery(connection, transaction, createSql);

        var legacyColumns = ReadColumns(connection, transaction, legacyTableName)
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectExpressions = columns
            .Select(column => legacyColumns.Contains(column) ? column : "'default'")
            .ToArray();

        ExecuteNonQuery(
            connection,
            transaction,
            $"""
            INSERT INTO {tableName} ({string.Join(", ", columns)})
            SELECT {string.Join(", ", selectExpressions)}
            FROM {legacyTableName};
            """);
        ExecuteNonQuery(connection, transaction, $"DROP TABLE {legacyTableName};");
    }

    private static List<ColumnInfo> ReadColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName)
    {
        using var tableInfo = connection.CreateCommand();
        tableInfo.Transaction = transaction;
        tableInfo.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = tableInfo.ExecuteReader();
        var columns = new List<ColumnInfo>();
        while (reader.Read())
            columns.Add(new ColumnInfo(reader.GetString(1), reader.GetInt32(5)));

        return columns;
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private sealed record ColumnInfo(string Name, int PrimaryKeyOrdinal);
}
