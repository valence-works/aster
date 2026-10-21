using Aster.Core.Models.Tenancy;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// The provider's schema, as the infrastructure steps <see cref="PostgresJsonbInfrastructureRunner"/> applies.
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

    /// <summary>
    /// The provider's infrastructure steps, in order. Applied steps are recorded, so a step never changes once
    /// shipped: later changes are new steps.
    /// </summary>
    public static IReadOnlyList<PostgresJsonbInfrastructureStep> Steps { get; } =
    [
        new("0001-initial-schema", "Tables for definitions, resource versions, activation state, lifecycle markers and shared aspects.", Ddl),
    ];
}
