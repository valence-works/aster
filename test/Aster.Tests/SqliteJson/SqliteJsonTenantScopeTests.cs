using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Querying;
using Aster.Tests.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonTenantScopeTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-tenant-scope-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    [Fact]
    public async Task Store_AllowsSameDefinitionAndResourceIdsAcrossTenants()
    {
        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var reader = provider.GetRequiredService<IResourceVersionReader>();

        await definitions.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA);
        await definitions.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantB);
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant A", TenantScopeTestFixtures.TenantA));
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant B", TenantScopeTestFixtures.TenantB));

        var tenantAResources = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
        })).ToList();
        var tenantBResources = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
        })).ToList();

        Assert.Equal(1, (await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA))!.Version);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantB))!.Version);
        Assert.Equal(TenantScopeTestFixtures.TenantA, tenantAResources.Single().TenantScope);
        Assert.Equal(TenantScopeTestFixtures.TenantB, tenantBResources.Single().TenantScope);
    }

    [Fact]
    public async Task ExistingPreTenantTables_ReadBackAsDefaultTenant()
    {
        var resource = TenantScopeTestFixtures.CreateResource("legacy-product", "Legacy");
        await CreateLegacyResourceVersionAsync(resource);

        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        var reader = provider.GetRequiredService<IResourceVersionReader>();

        var results = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest())).ToList();

        var result = Assert.Single(results);
        Assert.Equal("legacy-product", result.ResourceId);
        Assert.Equal("default", result.TenantScope.TenantId);
    }

    private async Task CreateLegacyResourceVersionAsync(Aster.Core.Models.Instances.Resource resource)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE resource_versions (
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
                $resourceId,
                $version,
                $id,
                $definitionId,
                $definitionVersion,
                $created,
                $owner,
                $hash,
                $payload
            );
            """;
        command.Parameters.AddWithValue("$resourceId", resource.ResourceId);
        command.Parameters.AddWithValue("$version", resource.Version);
        command.Parameters.AddWithValue("$id", resource.Id);
        command.Parameters.AddWithValue("$definitionId", resource.DefinitionId);
        command.Parameters.AddWithValue("$definitionVersion", resource.DefinitionVersion);
        command.Parameters.AddWithValue("$created", resource.Created.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$owner", DBNull.Value);
        command.Parameters.AddWithValue("$hash", DBNull.Value);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(resource, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        await command.ExecuteNonQueryAsync();
    }

    private static Aster.Core.Models.Definitions.ResourceDefinition CreateDefinition() =>
        new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
