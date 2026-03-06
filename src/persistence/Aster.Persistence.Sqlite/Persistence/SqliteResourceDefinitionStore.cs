using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Persistence.Sqlite.Internal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aster.Persistence.Sqlite.Persistence;

/// <summary>
/// Sqlite-backed implementation of <see cref="IResourceDefinitionStore"/>.
/// Stores each definition version as an immutable row with a full JSON payload.
/// </summary>
public sealed partial class SqliteResourceDefinitionStore : IResourceDefinitionStore
{
    private readonly SqlitePersistenceOptions options;
    private readonly ILogger<SqliteResourceDefinitionStore> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteResourceDefinitionStore"/> class.
    /// </summary>
    /// <param name="options">Sqlite persistence options.</param>
    /// <param name="logger">Logger instance.</param>
    public SqliteResourceDefinitionStore(
        SqlitePersistenceOptions options,
        ILogger<SqliteResourceDefinitionStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask RegisterDefinitionAsync(
        ResourceDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Determine next version
        var nextVersion = 1;
        await using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM ResourceDefinitionRecord WHERE DefinitionId = @defId";
            versionCmd.Parameters.AddWithValue("@defId", definition.DefinitionId);
            var maxVersion = await versionCmd.ExecuteScalarAsync(cancellationToken);
            nextVersion = Convert.ToInt32(maxVersion) + 1;
        }

        var versionedDef = definition with { Version = nextVersion };
        var payloadJson = JsonSerializer.Serialize(versionedDef, AsterJsonDefaults.Options);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ResourceDefinitionRecord (DefinitionId, Version, VersionId, IsSingleton, PayloadJson, CreatedUtc)
            VALUES (@defId, @version, @versionId, @isSingleton, @payload, @created)
            """;
        cmd.Parameters.AddWithValue("@defId", versionedDef.DefinitionId);
        cmd.Parameters.AddWithValue("@version", versionedDef.Version);
        cmd.Parameters.AddWithValue("@versionId", versionedDef.Id);
        cmd.Parameters.AddWithValue("@isSingleton", versionedDef.IsSingleton ? 1 : 0);
        cmd.Parameters.AddWithValue("@payload", payloadJson);
        cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        LogDefinitionRegistered(versionedDef.DefinitionId, versionedDef.Version);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PayloadJson FROM ResourceDefinitionRecord
            WHERE DefinitionId = @defId
            ORDER BY Version DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@defId", definitionId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            return null;

        return JsonSerializer.Deserialize<ResourceDefinition>((string)result, AsterJsonDefaults.Options);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(
        string definitionId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PayloadJson FROM ResourceDefinitionRecord
            WHERE DefinitionId = @defId AND Version = @version
            """;
        cmd.Parameters.AddWithValue("@defId", definitionId);
        cmd.Parameters.AddWithValue("@version", version);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            return null;

        return JsonSerializer.Deserialize<ResourceDefinition>((string)result, AsterJsonDefaults.Options);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Return latest version of each definition
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT r.PayloadJson FROM ResourceDefinitionRecord r
            INNER JOIN (
                SELECT DefinitionId, MAX(Version) AS MaxVersion
                FROM ResourceDefinitionRecord
                GROUP BY DefinitionId
            ) latest ON r.DefinitionId = latest.DefinitionId AND r.Version = latest.MaxVersion
            ORDER BY r.DefinitionId
            """;

        var results = new List<ResourceDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var json = reader.GetString(0);
            var def = JsonSerializer.Deserialize<ResourceDefinition>(json, AsterJsonDefaults.Options);
            if (def is not null)
                results.Add(def);
        }

        return results;
    }

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Registered definition '{DefinitionId}' version {Version}.")]
    private partial void LogDefinitionRegistered(string definitionId, int version);
}
