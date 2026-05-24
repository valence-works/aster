using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Instances;
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

        await definitions.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA, CancellationToken.None);
        await definitions.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantB, CancellationToken.None);
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

        Assert.Equal(1, (await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None))!.Version);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantB, CancellationToken.None))!.Version);
        Assert.Equal(TenantScopeTestFixtures.TenantA, tenantAResources.Single().TenantScope);
        Assert.Equal(TenantScopeTestFixtures.TenantB, tenantBResources.Single().TenantScope);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_WithoutTenantScope_AlwaysUsesDefaultTenant()
    {
        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var definition = CreateDefinition() with { TenantScope = TenantScopeTestFixtures.TenantA };

        await definitions.RegisterDefinitionAsync(definition);

        Assert.NotNull(await definitions.GetDefinitionAsync("Product"));
        Assert.Null(await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateActivationAsync_NormalizesPersistedPayloadIdentityToMethodArguments()
    {
        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var reader = provider.GetRequiredService<IResourceVersionReader>();
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("product-1", "Tenant A", TenantScopeTestFixtures.TenantA));

        var state = await writer.UpdateActivationAsync("product-1", "Published", new ActivationState
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ResourceId = "wrong-product",
            Channel = "Wrong",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });

        var active = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        })).ToList();

        Assert.Equal("product-1", state.ResourceId);
        Assert.Equal("Published", state.Channel);
        Assert.Equal("product-1", active.Single().ResourceId);
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

    [Fact]
    public async Task ExistingPreTenantTables_AreRebuiltForTenantAwareCollisionsAndUpserts()
    {
        await CreateLegacyTablesAsync();

        await using var provider = TenantScopeTestFixtures.CreateSqliteProvider(databasePath);
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var reader = provider.GetRequiredService<IResourceVersionReader>();

        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant A", TenantScopeTestFixtures.TenantA));
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant B", TenantScopeTestFixtures.TenantB));
        await writer.UpdateActivationAsync("shared-product", "Published", new ActivationState
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ResourceId = "shared-product",
            Channel = "Published",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });
        await writer.UpdateActivationAsync("shared-product", "Published", new ActivationState
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            ResourceId = "shared-product",
            Channel = "Published",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });

        var tenantAActive = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        })).ToList();
        var tenantBActive = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        })).ToList();

        Assert.Single(tenantAActive);
        Assert.Single(tenantBActive);
    }

    private async Task CreateLegacyResourceVersionAsync(Aster.Core.Models.Instances.Resource resource)
    {
        await CreateLegacyTablesAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
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

    private async Task CreateLegacyTablesAsync()
    {
        await using var connection = await OpenConnectionAsync();
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
            """;

        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        return connection;
    }

    private static Aster.Core.Models.Definitions.ResourceDefinition CreateDefinition() =>
        new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();

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
