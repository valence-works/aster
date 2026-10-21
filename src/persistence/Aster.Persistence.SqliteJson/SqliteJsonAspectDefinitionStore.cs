using System.Globalization;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// SQLite JSON implementation of <see cref="IAspectDefinitionStore"/>, over the additive
/// <c>aspect_definitions</c> table (ADR 0003).
/// </summary>
public sealed class SqliteJsonAspectDefinitionStore : IAspectDefinitionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonAspectDefinitionStore"/>, creating the schema when
    /// <see cref="SqliteJsonAsterOptions.InitializeSchema"/> is set (idempotent).
    /// </summary>
    /// <param name="options">The provider options.</param>
    public SqliteJsonAspectDefinitionStore(SqliteJsonAsterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        connectionString = options.ConnectionString;
        if (options.InitializeSchema)
            SqliteJsonSchema.Initialize(connectionString);
    }

    /// <inheritdoc />
    public async ValueTask<AspectDefinition> RegisterAsync(
        AspectDefinition aspect,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentException.ThrowIfNullOrWhiteSpace(aspect.AspectDefinitionId);
        FacetMetadataValidator.Validate(aspect);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var next = connection.CreateCommand();
        next.Transaction = transaction;
        next.CommandText = """
            SELECT COALESCE(MAX(version), 0) + 1
            FROM aspect_definitions
            WHERE tenant_id = $tenantId AND aspect_definition_id = $aspectDefinitionId;
            """;
        next.Parameters.AddWithValue("$tenantId", tenant.TenantId);
        next.Parameters.AddWithValue("$aspectDefinitionId", aspect.AspectDefinitionId);
        var version = Convert.ToInt32(await next.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        var registered = aspect with { Version = version, IsShared = false };

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO aspect_definitions (tenant_id, aspect_definition_id, version, id, payload)
            VALUES ($tenantId, $aspectDefinitionId, $version, $id, $payload);
            """;
        insert.Parameters.AddWithValue("$tenantId", tenant.TenantId);
        insert.Parameters.AddWithValue("$aspectDefinitionId", registered.AspectDefinitionId);
        insert.Parameters.AddWithValue("$version", registered.Version);
        insert.Parameters.AddWithValue("$id", registered.Id);
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(registered, JsonOptions));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return registered;
    }

    /// <inheritdoc />
    public async ValueTask<AspectDefinition?> GetLatestAsync(
        string aspectDefinitionId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);

        return (await ReadAsync(
            """
            SELECT payload
            FROM aspect_definitions
            WHERE tenant_id = $tenantId AND aspect_definition_id = $aspectDefinitionId
            ORDER BY version DESC
            LIMIT 1;
            """,
            tenantScope,
            command => command.Parameters.AddWithValue("$aspectDefinitionId", aspectDefinitionId),
            cancellationToken)).SingleOrDefault();
    }

    /// <inheritdoc />
    public async ValueTask<AspectDefinition?> GetVersionAsync(
        string aspectDefinitionId,
        int version,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);

        return (await ReadAsync(
            """
            SELECT payload
            FROM aspect_definitions
            WHERE tenant_id = $tenantId AND aspect_definition_id = $aspectDefinitionId AND version = $version;
            """,
            tenantScope,
            command =>
            {
                command.Parameters.AddWithValue("$aspectDefinitionId", aspectDefinitionId);
                command.Parameters.AddWithValue("$version", version);
            },
            cancellationToken)).SingleOrDefault();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AspectDefinition>> ListAsync(
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(
            """
            SELECT ad.payload
            FROM aspect_definitions ad
            INNER JOIN (
                SELECT aspect_definition_id, MAX(version) AS version
                FROM aspect_definitions
                WHERE tenant_id = $tenantId
                GROUP BY aspect_definition_id
            ) latest
                ON latest.aspect_definition_id = ad.aspect_definition_id
                AND latest.version = ad.version
            WHERE ad.tenant_id = $tenantId;
            """,
            tenantScope,
            static _ => { },
            cancellationToken))
        .OrderBy(static aspect => aspect.AspectDefinitionId, StringComparer.Ordinal)
        .ToList();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AspectDefinition>> ListVersionsAsync(
        string aspectDefinitionId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);

        return await ReadAsync(
            """
            SELECT payload
            FROM aspect_definitions
            WHERE tenant_id = $tenantId AND aspect_definition_id = $aspectDefinitionId
            ORDER BY version;
            """,
            tenantScope,
            command => command.Parameters.AddWithValue("$aspectDefinitionId", aspectDefinitionId),
            cancellationToken);
    }

    private async Task<List<AspectDefinition>> ReadAsync(
        string commandText,
        TenantScope? tenantScope,
        Action<SqliteCommand> addParameters,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$tenantId", tenant.TenantId);
        addParameters(command);

        var results = new List<AspectDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(JsonSerializer.Deserialize<AspectDefinition>(reader.GetString(0), JsonOptions)!);

        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
