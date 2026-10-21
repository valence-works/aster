using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.Data.Sqlite;

namespace Aster.Tests.SqliteJson;

/// <summary>
/// #169: the latest version of a bounded set of resources is read by key, not by walking the tenant.
/// </summary>
public sealed class SqliteJsonLatestByIdReadTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-latest-{Guid.NewGuid():N}.db");

    private SqliteJsonResourceStore CreateStore() =>
        new(new SqliteJsonAsterOptions { ConnectionString = $"Data Source={databasePath}" });

    [Fact]
    public async Task LatestByIds_ReturnsEachResourcesLatestVersion_AndSkipsMissingIds()
    {
        var store = CreateStore();
        await SaveAsync(store, "a", 1);
        await SaveAsync(store, "a", 2);
        await SaveAsync(store, "b", 1);
        await SaveAsync(store, "c", 1);
        await SaveAsync(store, "a", 1, TenantScope.FromTenantId("other"));
        await SaveAsync(store, "a", 5, TenantScope.FromTenantId("other"));

        var latest = await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Latest,
            ResourceIds = ["b", "missing", "a"],
        });

        Assert.Equal(new[] { ("a", 2), ("b", 1) }, latest.Select(static r => (r.ResourceId, r.Version)));
    }

    [Fact]
    public void LatestByIds_QueryPlan_SeeksByResourceId()
    {
        _ = CreateStore();
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + SqliteJsonResourceStore.LatestVersionsByResourceIdsSql(["$resourceId0", "$resourceId1"]);
        command.Parameters.AddWithValue("$tenantId", TenantScope.DefaultTenantId);
        command.Parameters.AddWithValue("$resourceId0", "a");
        command.Parameters.AddWithValue("$resourceId1", "b");

        var plan = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                plan.Add(reader.GetString(3));
        }

        // Every access to resource_versions must seek by (tenant_id, resource_id); a bare
        // (tenant_id=?) search or a SCAN walks the whole tenant.
        var tableAccesses = plan.Where(static detail => detail.StartsWith("SEARCH", StringComparison.Ordinal) || detail.StartsWith("SCAN", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(tableAccesses);
        Assert.All(tableAccesses, detail => Assert.Contains("tenant_id=? AND resource_id=?", detail));
    }

    private static Task<Resource> SaveAsync(SqliteJsonResourceStore store, string resourceId, int version, TenantScope? tenant = null) =>
        store.SaveVersionAsync(new Resource
        {
            ResourceId = resourceId,
            Id = $"{resourceId}-{version}-{tenant?.TenantId}",
            DefinitionId = "Article",
            Version = version,
            Created = DateTime.UtcNow,
            TenantScope = tenant ?? TenantScope.Default,
        }).AsTask();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
