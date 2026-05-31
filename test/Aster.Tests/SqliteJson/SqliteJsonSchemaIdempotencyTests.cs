using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonSchemaIdempotencyTests : IDisposable
{
    private readonly List<string> databasePaths = [];

    public void Dispose()
    {
        foreach (var databasePath in databasePaths)
            DeleteSqliteFiles(databasePath);
    }

    [Fact]
    public async Task RepeatedProviderInitialization_PreservesPersistedStateAndTenantAwareKeys()
    {
        var databasePath = NewDatabasePath("aster-schema-idempotency");

        await using (var first = CreateProvider(databasePath))
        {
            var definitions = first.GetRequiredService<IResourceDefinitionStore>();
            var manager = first.GetRequiredService<IResourceManager>();
            var markers = first.GetRequiredService<IResourceLifecycleMarkerService>();

            await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId("Product")
                .Build());

            var resource = await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["Title"] = new { Title = "Durable" },
                },
            });

            await manager.ActivateAsync(resource.ResourceId, resource.Version, "Published");
            await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                ResourceId = resource.ResourceId,
                State = ResourceLifecycleMarkerState.Archived,
                MarkedAt = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
            });
        }

        await using (var second = CreateProvider(databasePath))
        {
            var reader = second.GetRequiredService<IResourceVersionReader>();
            var markerStore = second.GetRequiredService<IResourceLifecycleMarkerStore>();

            var active = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                Scope = ResourceVersionScope.Active,
                ActivationChannel = "Published",
            })).ToList();

            var resource = Assert.Single(active);
            Assert.Equal("Product", resource.DefinitionId);
            Assert.Equal(
                ResourceLifecycleMarkerState.Archived,
                (await markerStore.GetMarkerAsync(resource.ResourceId, TenantScope.Default))!.State);
        }

        await using (var third = CreateProvider(databasePath))
        {
            var definitions = third.GetRequiredService<IResourceDefinitionStore>();
            Assert.NotNull(await definitions.GetDefinitionAsync("Product"));
        }

        AssertPrimaryKey(databasePath, "resource_definitions", ["tenant_id", "definition_id", "version"]);
        AssertPrimaryKey(databasePath, "resource_versions", ["tenant_id", "resource_id", "version"]);
        AssertPrimaryKey(databasePath, "activation_states", ["tenant_id", "resource_id", "channel"]);
        AssertPrimaryKey(databasePath, "lifecycle_markers", ["tenant_id", "resource_id"]);
    }

    [Fact]
    public async Task LegacyTenantUpgrade_CanBeInitializedRepeatedlyWithoutDuplicateRowsOrBootstrapTables()
    {
        var databasePath = NewDatabasePath("aster-legacy-idempotency");
        await CreateLegacyTablesAndResourceAsync(databasePath);

        await using (var first = CreateProvider(databasePath))
            _ = first.GetRequiredService<IResourceVersionReader>();
        await using (var second = CreateProvider(databasePath))
            _ = second.GetRequiredService<IResourceVersionReader>();

        Assert.Equal(1, CountRows(databasePath, "resource_versions"));
        Assert.Equal(1, CountRows(
            databasePath,
            "resource_versions",
            "tenant_id = 'default' AND resource_id = 'legacy-product' AND version = 1"));
        Assert.DoesNotContain(
            ReadTableNames(databasePath),
            static name => name.Contains("__legacy_tenant_bootstrap", StringComparison.Ordinal));

        AssertPrimaryKey(databasePath, "resource_definitions", ["tenant_id", "definition_id", "version"]);
        AssertPrimaryKey(databasePath, "resource_versions", ["tenant_id", "resource_id", "version"]);
        AssertPrimaryKey(databasePath, "activation_states", ["tenant_id", "resource_id", "channel"]);
        AssertPrimaryKey(databasePath, "lifecycle_markers", ["tenant_id", "resource_id"]);
    }

    [Fact]
    public void InitializeSchemaFalse_IdentityAndCapabilitiesResolutionDoesNotCreateDatabase()
    {
        var databasePath = NewDatabasePath("aster-no-init");
        var services = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={databasePath}";
                options.InitializeSchema = false;
            });

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            SqliteJsonQueryCapabilitiesProvider.ProviderKey,
            provider.GetRequiredService<IResourceQueryProviderIdentity>().ProviderKey);
        Assert.Equal(
            SqliteJsonQueryCapabilitiesProvider.ProviderKey,
            provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities.ProviderKey);
        Assert.False(File.Exists(databasePath));
    }

    private string NewDatabasePath(string prefix)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
        databasePaths.Add(databasePath);
        return databasePath;
    }

    private static ServiceProvider CreateProvider(string databasePath) =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}")
            .BuildServiceProvider();

    private static async Task CreateLegacyTablesAndResourceAsync(string databasePath)
    {
        await using var connection = await OpenConnectionAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
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

            CREATE TABLE IF NOT EXISTS activation_states (
                resource_id TEXT NOT NULL,
                channel TEXT NOT NULL,
                payload TEXT NOT NULL,
                PRIMARY KEY (resource_id, channel)
            );

            INSERT INTO resource_versions (
                resource_id,
                version,
                id,
                definition_id,
                definition_version,
                created,
                owner,
                hash,
                payload
            )
            VALUES (
                'legacy-product',
                1,
                'legacy-product:1',
                'Product',
                NULL,
                '2026-05-31T00:00:00.0000000Z',
                NULL,
                NULL,
                '{"resourceId":"legacy-product","id":"legacy-product:1","definitionId":"Product","version":1,"created":"2026-05-31T00:00:00Z"}'
            );
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static void AssertPrimaryKey(string databasePath, string tableName, string[] expectedColumns)
    {
        var actual = ReadPrimaryKey(databasePath, tableName);
        Assert.Equal(expectedColumns, actual);
    }

    private static string[] ReadPrimaryKey(string databasePath, string tableName)
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

    private static int CountRows(string databasePath, string tableName, string? whereClause = null)
    {
        using var connection = OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = whereClause is null
            ? $"SELECT COUNT(*) FROM {tableName};"
            : $"SELECT COUNT(*) FROM {tableName} WHERE {whereClause};";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static IReadOnlyList<string> ReadTableNames(string databasePath)
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

    private static async Task<SqliteConnection> OpenConnectionAsync(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        return connection;
    }

    private static SqliteConnection OpenConnection(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    private static void DeleteSqliteFiles(string databasePath)
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
