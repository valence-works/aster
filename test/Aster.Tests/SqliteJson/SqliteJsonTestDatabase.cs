using Microsoft.Data.Sqlite;

namespace Aster.Tests.SqliteJson;

internal static class SqliteJsonTestDatabase
{
    public static async Task<SqliteConnection> OpenConnectionAsync(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        return connection;
    }

    public static SqliteConnection OpenConnection(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    public static void AssertPrimaryKey(string databasePath, string tableName, string[] expectedColumns)
    {
        var actual = ReadPrimaryKey(databasePath, tableName);
        Assert.Equal(expectedColumns, actual);
    }

    public static string[] ReadPrimaryKey(string databasePath, string tableName)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        var columns = new List<(string Name, int Ordinal)>();
        while (reader.Read())
        {
            var ordinal = reader.GetInt32(5);
            if (ordinal > 0)
                columns.Add((reader.GetString(1), ordinal));
        }

        return columns
            .OrderBy(static column => column.Ordinal)
            .Select(static column => column.Name)
            .ToArray();
    }

    public static int CountRows(string databasePath, string tableName, string? whereClause = null)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = whereClause is null
            ? $"SELECT COUNT(*) FROM {tableName};"
            : $"SELECT COUNT(*) FROM {tableName} WHERE {whereClause};";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static IReadOnlyList<string> ReadTableNames(string databasePath)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }

    public static void DeleteFiles(string databasePath)
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
