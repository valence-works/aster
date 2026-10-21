using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.Data.Sqlite;

namespace Aster.Tests.SqliteJson;

/// <summary>
/// #171: reads of many composite keys (resource versions, activation states, definition and aspect versions)
/// seek each key through the primary key instead of walking the tenant.
/// </summary>
public sealed class SqliteJsonCompositeKeyReadTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-keys-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData("resource_versions", "resource_id", "version")]
    [InlineData("activation_states", "resource_id", "channel")]
    [InlineData("resource_definitions", "definition_id", "version")]
    [InlineData("aspect_definitions", "aspect_definition_id", "version")]
    public void ManyKeys_QueryPlan_SeeksEachKey(string table, string first, string second)
    {
        // An OR of 10+ key predicates is where SQLite without statistics fell back to a tenant walk.
        const int keyCount = 50;
        _ = new SqliteJsonResourceStore(new SqliteJsonAsterOptions { ConnectionString = $"Data Source={databasePath}" });
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + SqliteJsonResourceStore.CompositeKeyPayloadsSql(table, (first, second), keyCount);
        command.Parameters.AddWithValue("$tenantId", TenantScope.DefaultTenantId);
        for (var index = 0; index < keyCount; index++)
        {
            command.Parameters.AddWithValue($"$keyFirst{index}", $"key-{index}");
            command.Parameters.AddWithValue($"$keySecond{index}", second == "channel" ? "published" : index + 1);
        }

        var plan = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                plan.Add(reader.GetString(3));
        }

        var tableAccess = Assert.Single(plan, static detail => detail.Contains(" t USING", StringComparison.Ordinal) || detail.StartsWith("SCAN t", StringComparison.Ordinal));
        Assert.Contains($"tenant_id=? AND {first}=? AND {second}=?", tableAccess);
    }

    public void Dispose()
    {
        // Only this test's pool: clearing every pool disposes connections that parallel tests are using (#213).
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            SqliteConnection.ClearPool(connection);
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
