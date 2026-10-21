using Aster.Core.Models.Tenancy;
using Npgsql;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// Creates the provider's tables. Bootstrap is idempotent and runs under a transaction-scoped advisory lock,
/// so processes starting at once against a fresh database serialize instead of racing on the catalog
/// (<c>CREATE TABLE IF NOT EXISTS</c> alone can fail with a unique violation in <c>pg_type</c>).
/// </summary>
internal static class PostgresJsonbSchema
{
    // Identifier columns use the "C" collation: byte order, which matches the ordinal ordering the stores
    // and the other providers return, and keeps primary-key comparisons free of locale rules.
    private const string Id = "text COLLATE \"C\" NOT NULL";

    private static readonly string Ddl = $"""
        CREATE TABLE IF NOT EXISTS resource_definitions (
            tenant_id {Id} DEFAULT '{TenantScope.DefaultTenantId}',
            definition_id {Id},
            version integer NOT NULL,
            id text NOT NULL,
            payload jsonb NOT NULL,
            PRIMARY KEY (tenant_id, definition_id, version)
        );

        CREATE TABLE IF NOT EXISTS resource_versions (
            tenant_id {Id} DEFAULT '{TenantScope.DefaultTenantId}',
            resource_id {Id},
            version integer NOT NULL,
            id text NOT NULL,
            definition_id {Id},
            definition_version integer NULL,
            created timestamptz NOT NULL,
            owner text NULL,
            hash text NULL,
            payload jsonb NOT NULL,
            PRIMARY KEY (tenant_id, resource_id, version)
        );

        CREATE INDEX IF NOT EXISTS ix_resource_versions_definition_id
            ON resource_versions (tenant_id, definition_id);

        CREATE TABLE IF NOT EXISTS activation_states (
            tenant_id {Id} DEFAULT '{TenantScope.DefaultTenantId}',
            resource_id {Id},
            channel {Id},
            payload jsonb NOT NULL,
            PRIMARY KEY (tenant_id, resource_id, channel)
        );

        CREATE TABLE IF NOT EXISTS lifecycle_markers (
            tenant_id {Id} DEFAULT '{TenantScope.DefaultTenantId}',
            resource_id {Id},
            payload jsonb NOT NULL,
            PRIMARY KEY (tenant_id, resource_id)
        );

        CREATE TABLE IF NOT EXISTS aspect_definitions (
            tenant_id {Id} DEFAULT '{TenantScope.DefaultTenantId}',
            aspect_definition_id {Id},
            version integer NOT NULL,
            id text NOT NULL,
            payload jsonb NOT NULL,
            PRIMARY KEY (tenant_id, aspect_definition_id, version)
        );
        """;

    // An arbitrary constant shared by every Aster process, so concurrent bootstraps of one database serialize.
    private const long BootstrapLockKey = 0x4153_5445_5200_0001; // "ASTER", schema generation 1

    /// <summary>
    /// Creates any missing table or index. Safe to call repeatedly and from several processes at once.
    /// </summary>
    public static void Initialize(NpgsqlDataSource dataSource)
    {
        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key);", connection, transaction))
        {
            command.Parameters.AddWithValue("key", BootstrapLockKey);
            command.ExecuteNonQuery();
        }

        using (var command = new NpgsqlCommand(Ddl, connection, transaction))
            command.ExecuteNonQuery();

        transaction.Commit();
    }
}
