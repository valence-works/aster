using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqlServerJson.Querying;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// The provider's schema, as the infrastructure steps <see cref="SqlServerJsonInfrastructureRunner"/> applies.
/// </summary>
internal static class SqlServerJsonSchema
{
    // Identifier columns compare and sort by code point (BIN2), which matches the ordinal comparisons and ordering
    // the stores and the other providers use. Key lengths keep every primary key under the 900-byte clustered limit.
    private const string Binary = SqlServerText.BinaryCollation;
    private const string Tenant = $"nvarchar(128) COLLATE {Binary} NOT NULL DEFAULT N'{TenantScope.DefaultTenantId}'";
    private const string Key = $"nvarchar(256) COLLATE {Binary} NOT NULL";

    // SQL Server compares strings as if padded with trailing spaces, even under BIN2, so 'x' and 'x ' would be one key
    // in every primary key and lookup. Identifier columns therefore refuse trailing spaces (see the provider README).
    private static string NoTrailingSpaces(string column) => $"CHECK (DATALENGTH({column}) = DATALENGTH(RTRIM({column})))";

    // Every statement checks the catalog first, so the step can run over a schema created before the runner.
    private static readonly string Tables = $"""
        IF OBJECT_ID(N'resource_definitions', N'U') IS NULL
        CREATE TABLE resource_definitions (
            tenant_id {Tenant} {NoTrailingSpaces("tenant_id")},
            definition_id {Key} {NoTrailingSpaces("definition_id")},
            version int NOT NULL,
            id {Key},
            payload nvarchar(max) NOT NULL,
            CONSTRAINT pk_resource_definitions PRIMARY KEY (tenant_id, definition_id, version)
        );

        IF OBJECT_ID(N'resource_versions', N'U') IS NULL
        CREATE TABLE resource_versions (
            tenant_id {Tenant} {NoTrailingSpaces("tenant_id")},
            resource_id {Key} {NoTrailingSpaces("resource_id")},
            version int NOT NULL,
            id {Key},
            definition_id {Key} {NoTrailingSpaces("definition_id")},
            definition_version int NULL,
            created datetime2(7) NOT NULL,
            owner nvarchar(max) COLLATE {Binary} NULL,
            hash nvarchar(max) NULL,
            payload nvarchar(max) NOT NULL,
            CONSTRAINT pk_resource_versions PRIMARY KEY (tenant_id, resource_id, version)
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'resource_versions') AND name = N'ix_resource_versions_definition_id')
        CREATE INDEX ix_resource_versions_definition_id ON resource_versions (tenant_id, definition_id);

        IF OBJECT_ID(N'activation_states', N'U') IS NULL
        CREATE TABLE activation_states (
            tenant_id {Tenant} {NoTrailingSpaces("tenant_id")},
            resource_id {Key} {NoTrailingSpaces("resource_id")},
            channel nvarchar(64) COLLATE {Binary} NOT NULL {NoTrailingSpaces("channel")},
            payload nvarchar(max) NOT NULL,
            CONSTRAINT pk_activation_states PRIMARY KEY (tenant_id, resource_id, channel)
        );

        IF OBJECT_ID(N'lifecycle_markers', N'U') IS NULL
        CREATE TABLE lifecycle_markers (
            tenant_id {Tenant} {NoTrailingSpaces("tenant_id")},
            resource_id {Key} {NoTrailingSpaces("resource_id")},
            payload nvarchar(max) NOT NULL,
            CONSTRAINT pk_lifecycle_markers PRIMARY KEY (tenant_id, resource_id)
        );

        IF OBJECT_ID(N'aspect_definitions', N'U') IS NULL
        CREATE TABLE aspect_definitions (
            tenant_id {Tenant} {NoTrailingSpaces("tenant_id")},
            aspect_definition_id {Key} {NoTrailingSpaces("aspect_definition_id")},
            version int NOT NULL,
            id {Key},
            payload nvarchar(max) NOT NULL,
            CONSTRAINT pk_aspect_definitions PRIMARY KEY (tenant_id, aspect_definition_id, version)
        );

        -- One row per string element of each version's KeywordArray projections, kept by a trigger the runner
        -- writes from the declaration (SqlServerProjections). Deleting a version deletes its rows.
        IF OBJECT_ID(N'aster_projection_elements', N'U') IS NULL
        CREATE TABLE aster_projection_elements (
            tenant_id nvarchar(128) COLLATE {Binary} NOT NULL,
            resource_id {Key},
            version int NOT NULL,
            projection_id nvarchar(40) COLLATE {Binary} NOT NULL,
            ordinal int NOT NULL,
            element_key nvarchar({SqlServerText.KeyLength}) COLLATE {Binary} NOT NULL,
            CONSTRAINT pk_aster_projection_elements PRIMARY KEY (tenant_id, resource_id, version, projection_id, ordinal),
            CONSTRAINT fk_aster_projection_elements_version FOREIGN KEY (tenant_id, resource_id, version)
                REFERENCES resource_versions (tenant_id, resource_id, version) ON DELETE CASCADE
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'aster_projection_elements') AND name = N'ix_aster_projection_elements_key')
        CREATE INDEX ix_aster_projection_elements_key ON aster_projection_elements (projection_id, tenant_id, element_key);

        -- The KeywordArray projections whose element rows exist, so the runner can tell which to add and remove.
        IF OBJECT_ID(N'aster_array_projections', N'U') IS NULL
        CREATE TABLE aster_array_projections (
            projection_id nvarchar(40) COLLATE {Binary} NOT NULL CONSTRAINT pk_aster_array_projections PRIMARY KEY,
            description nvarchar(max) NOT NULL
        );
        """;

    /// <summary>
    /// The provider's infrastructure steps, in order. Applied steps are recorded, so a step never changes once
    /// shipped: later changes are new steps.
    /// </summary>
    public static IReadOnlyList<SqlServerJsonInfrastructureStep> Steps { get; } =
    [
        new("0001-initial-schema", "Tables for definitions, resource versions, activation state, lifecycle markers, shared aspects and array projection elements.", [Tables]),
        new("0002-query-functions", "The deterministic date-key function that queries and projection indexes share.", [SqlServerDateKey.CreateFunction]),
    ];
}
