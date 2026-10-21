using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Persistence.Relational;

/// <summary>
/// <see cref="IAspectDefinitionStore"/> over a relational database's additive <c>aspect_definitions</c> table
/// (ADR 0003). A <see cref="RelationalJsonDialect"/> supplies what differs between databases.
/// </summary>
internal abstract class RelationalJsonAspectDefinitionStore : IAspectDefinitionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RelationalJsonDialect dialect;

    /// <summary>
    /// Initializes a new instance of <see cref="RelationalJsonAspectDefinitionStore"/>.
    /// </summary>
    /// <param name="dialect">The provider's database dialect.</param>
    protected RelationalJsonAspectDefinitionStore(RelationalJsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        this.dialect = dialect;
    }

    /// <inheritdoc />
    public async ValueTask<AspectDefinition> RegisterAsync(
        AspectDefinition aspect,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default) =>
        (await RegisterAsync(aspect, expectedLatestVersion: null, tenantScope, cancellationToken)).Aspect!;

    /// <inheritdoc />
    public ValueTask<AspectDefinitionRegistrationResult> RegisterIfLatestAsync(
        AspectDefinition aspect,
        int expectedLatestVersion,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLatestVersion);
        return RegisterAsync(aspect, expectedLatestVersion, tenantScope, cancellationToken);
    }

    // The latest-version read, the check and the insert run under the aspect's serialized write, so two
    // registrations based on the same version can't both append.
    private async ValueTask<AspectDefinitionRegistrationResult> RegisterAsync(
        AspectDefinition aspect,
        int? expectedLatestVersion,
        TenantScope? tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentException.ThrowIfNullOrWhiteSpace(aspect.AspectDefinitionId);
        FacetMetadataValidator.Validate(aspect);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await dialect.BeginSerializedWriteAsync(
            connection,
            $"aspect_definitions/{tenant.TenantId}/{aspect.AspectDefinitionId}",
            cancellationToken);

        await using var latestCommand = connection.CreateCommand();
        latestCommand.Transaction = transaction;
        latestCommand.CommandText = """
            SELECT COALESCE(MAX(version), 0)
            FROM aspect_definitions
            WHERE tenant_id = @tenantId AND aspect_definition_id = @aspectDefinitionId;
            """;
        Add(latestCommand, "@tenantId", tenant.TenantId);
        Add(latestCommand, "@aspectDefinitionId", aspect.AspectDefinitionId);
        var latest = Convert.ToInt32(await latestCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (expectedLatestVersion is { } expected && expected != latest)
        {
            await transaction.CommitAsync(cancellationToken);
            return new() { Status = AspectDefinitionRegistrationStatus.VersionConflict, LatestVersion = latest };
        }

        var registered = aspect with { Version = latest + 1, IsShared = false };

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO aspect_definitions (tenant_id, aspect_definition_id, version, id, payload)
            VALUES (@tenantId, @aspectDefinitionId, @version, @id, {dialect.JsonParameter("@payload")});
            """;
        Add(insert, "@tenantId", tenant.TenantId);
        Add(insert, "@aspectDefinitionId", registered.AspectDefinitionId);
        Add(insert, "@version", registered.Version);
        Add(insert, "@id", registered.Id);
        Add(insert, "@payload", JsonSerializer.Serialize(registered, JsonOptions));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new() { Status = AspectDefinitionRegistrationStatus.Registered, Aspect = registered, LatestVersion = registered.Version };
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
            WHERE tenant_id = @tenantId AND aspect_definition_id = @aspectDefinitionId
            ORDER BY version DESC
            LIMIT 1;
            """,
            tenantScope,
            command => Add(command, "@aspectDefinitionId", aspectDefinitionId),
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
            WHERE tenant_id = @tenantId AND aspect_definition_id = @aspectDefinitionId AND version = @version;
            """,
            tenantScope,
            command =>
            {
                Add(command, "@aspectDefinitionId", aspectDefinitionId);
                Add(command, "@version", version);
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
                WHERE tenant_id = @tenantId
                GROUP BY aspect_definition_id
            ) latest
                ON latest.aspect_definition_id = ad.aspect_definition_id
                AND latest.version = ad.version
            WHERE ad.tenant_id = @tenantId;
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
            WHERE tenant_id = @tenantId AND aspect_definition_id = @aspectDefinitionId
            ORDER BY version;
            """,
            tenantScope,
            command => Add(command, "@aspectDefinitionId", aspectDefinitionId),
            cancellationToken);
    }

    private async Task<List<AspectDefinition>> ReadAsync(
        string commandText,
        TenantScope? tenantScope,
        Action<DbCommand> addParameters,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        Add(command, "@tenantId", tenant.TenantId);
        addParameters(command);

        var results = new List<AspectDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(JsonSerializer.Deserialize<AspectDefinition>(reader.GetString(0), JsonOptions)!);

        return results;
    }

    private ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        dialect.OpenConnectionAsync(cancellationToken);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
