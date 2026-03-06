using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Persistence.Sqlite.Internal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aster.Persistence.Sqlite.Persistence;

/// <summary>
/// Sqlite-backed implementation of <see cref="IResourceWriteStore"/>.
/// Provides append-only version persistence and durable activation state management.
/// </summary>
public sealed partial class SqliteResourceWriteStore : IResourceWriteStore
{
    private readonly SqlitePersistenceOptions options;
    private readonly ILogger<SqliteResourceWriteStore> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteResourceWriteStore"/> class.
    /// </summary>
    /// <param name="options">Sqlite persistence options.</param>
    /// <param name="logger">Logger instance.</param>
    public SqliteResourceWriteStore(
        SqlitePersistenceOptions options,
        ILogger<SqliteResourceWriteStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<Resource> SaveVersionAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var aspectsJson = JsonSerializer.Serialize(resource.Aspects, AsterJsonDefaults.Options);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Check for singleton violation on V1
        if (resource.Version == 1)
        {
            await using var singletonCmd = connection.CreateCommand();
            singletonCmd.CommandText = """
                SELECT COUNT(1) FROM ResourceRecord WHERE ResourceId = @resId
                """;
            singletonCmd.Parameters.AddWithValue("@resId", resource.ResourceId);
            var existingCount = Convert.ToInt32(await singletonCmd.ExecuteScalarAsync(cancellationToken));
            if (existingCount > 0)
                throw new DuplicateResourceIdException(resource.ResourceId);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ResourceRecord (ResourceId, Version, VersionId, DefinitionId, DefinitionVersion, AspectsJson, CreatedUtc, Owner, Hash)
            VALUES (@resId, @version, @versionId, @defId, @defVersion, @aspects, @created, @owner, @hash)
            """;
        cmd.Parameters.AddWithValue("@resId", resource.ResourceId);
        cmd.Parameters.AddWithValue("@version", resource.Version);
        cmd.Parameters.AddWithValue("@versionId", resource.Id);
        cmd.Parameters.AddWithValue("@defId", resource.DefinitionId);
        cmd.Parameters.AddWithValue("@defVersion", resource.DefinitionVersion.HasValue ? resource.DefinitionVersion.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@aspects", aspectsJson);
        cmd.Parameters.AddWithValue("@created", resource.Created.ToString("O"));
        cmd.Parameters.AddWithValue("@owner", (object?)resource.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hash", (object?)resource.Hash ?? DBNull.Value);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            throw new ConcurrencyException(
                $"Version {resource.Version} of resource '{resource.ResourceId}' already exists.", ex);
        }

        LogResourceSaved(resource.ResourceId, resource.Version, resource.Id);
        return resource;
    }

    /// <inheritdoc />
    public async ValueTask<ActivationState> UpdateActivationAsync(
        string resourceId,
        string channel,
        ActivationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(state);

        var activeVersionsJson = JsonSerializer.Serialize(state.ActiveVersions, AsterJsonDefaults.Options);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ActivationRecord (ResourceId, Channel, Mode, ActiveVersionsJson, LastUpdatedUtc)
            VALUES (@resId, @channel, @mode, @versions, @updated)
            ON CONFLICT(ResourceId, Channel) DO UPDATE SET
                Mode = excluded.Mode,
                ActiveVersionsJson = excluded.ActiveVersionsJson,
                LastUpdatedUtc = excluded.LastUpdatedUtc
            """;
        cmd.Parameters.AddWithValue("@resId", resourceId);
        cmd.Parameters.AddWithValue("@channel", channel);
        cmd.Parameters.AddWithValue("@mode", state.Mode.ToString());
        cmd.Parameters.AddWithValue("@versions", activeVersionsJson);
        cmd.Parameters.AddWithValue("@updated", state.LastUpdated.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        LogActivationUpdated(resourceId, channel, state.Mode.ToString());
        return state;
    }

    /// <summary>
    /// Retrieves a specific version of a resource, or <see langword="null"/> if not found.
    /// </summary>
    public async ValueTask<Resource?> GetVersionAsync(
        string resourceId,
        int version,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ResourceId, Version, VersionId, DefinitionId, DefinitionVersion, AspectsJson, CreatedUtc, Owner, Hash
            FROM ResourceRecord
            WHERE ResourceId = @resId AND Version = @version
            """;
        cmd.Parameters.AddWithValue("@resId", resourceId);
        cmd.Parameters.AddWithValue("@version", version);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadResource(reader);
    }

    /// <summary>
    /// Returns all versions of the specified resource, ordered by version.
    /// </summary>
    public async ValueTask<IEnumerable<Resource>> GetVersionsAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ResourceId, Version, VersionId, DefinitionId, DefinitionVersion, AspectsJson, CreatedUtc, Owner, Hash
            FROM ResourceRecord
            WHERE ResourceId = @resId
            ORDER BY Version ASC
            """;
        cmd.Parameters.AddWithValue("@resId", resourceId);

        var resources = new List<Resource>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            resources.Add(ReadResource(reader));

        return resources;
    }

    /// <summary>
    /// Returns the latest version of a resource, or <see langword="null"/> if not found.
    /// </summary>
    public async ValueTask<Resource?> GetLatestVersionAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ResourceId, Version, VersionId, DefinitionId, DefinitionVersion, AspectsJson, CreatedUtc, Owner, Hash
            FROM ResourceRecord
            WHERE ResourceId = @resId
            ORDER BY Version DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@resId", resourceId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadResource(reader);
    }

    /// <summary>
    /// Returns the activation state for a resource channel, or <see langword="null"/> if not found.
    /// </summary>
    public async ValueTask<ActivationState?> GetActivationStateAsync(
        string resourceId,
        string channel,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ResourceId, Channel, Mode, ActiveVersionsJson, LastUpdatedUtc
            FROM ActivationRecord
            WHERE ResourceId = @resId AND Channel = @channel
            """;
        cmd.Parameters.AddWithValue("@resId", resourceId);
        cmd.Parameters.AddWithValue("@channel", channel);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadActivationState(reader);
    }

    /// <summary>
    /// Returns all active resource versions in the specified channel.
    /// </summary>
    public async ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(
        string resourceId,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var state = await GetActivationStateAsync(resourceId, channel, cancellationToken);
        if (state is null || state.ActiveVersions.Count == 0)
            return [];

        var resources = new List<Resource>();
        foreach (var version in state.ActiveVersions)
        {
            var resource = await GetVersionAsync(resourceId, version, cancellationToken);
            if (resource is not null)
                resources.Add(resource);
        }

        return resources;
    }

    /// <summary>
    /// Returns the maximum version number for a resource, or 0 if no versions exist.
    /// </summary>
    public async ValueTask<int> GetMaxVersionAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM ResourceRecord WHERE ResourceId = @resId";
        cmd.Parameters.AddWithValue("@resId", resourceId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Checks whether any resource exists for the given definition ID.
    /// </summary>
    public async ValueTask<bool> AnyResourceExistsForDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM ResourceRecord WHERE DefinitionId = @defId AND Version = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("@defId", definitionId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static Resource ReadResource(SqliteDataReader reader)
    {
        var aspectsJson = reader.GetString(5);
        var aspects = JsonSerializer.Deserialize<Dictionary<string, object>>(aspectsJson, AsterJsonDefaults.Options)
                      ?? new Dictionary<string, object>();

        return new Resource
        {
            ResourceId = reader.GetString(0),
            Version = reader.GetInt32(1),
            Id = reader.GetString(2),
            DefinitionId = reader.GetString(3),
            DefinitionVersion = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Aspects = aspects,
            Created = DateTime.Parse(reader.GetString(6)),
            Owner = reader.IsDBNull(7) ? null : reader.GetString(7),
            Hash = reader.IsDBNull(8) ? null : reader.GetString(8),
        };
    }

    private static ActivationState ReadActivationState(SqliteDataReader reader)
    {
        var modeStr = reader.GetString(2);
        var mode = Enum.Parse<ChannelMode>(modeStr, ignoreCase: true);
        var versionsJson = reader.GetString(3);
        var activeVersions = JsonSerializer.Deserialize<List<int>>(versionsJson, AsterJsonDefaults.Options) ?? [];

        return new ActivationState
        {
            ResourceId = reader.GetString(0),
            Channel = reader.GetString(1),
            Mode = mode,
            ActiveVersions = activeVersions,
            LastUpdated = DateTime.Parse(reader.GetString(4)),
        };
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "Saved resource '{ResourceId}' version {Version} (Id={Id}).")]
    private partial void LogResourceSaved(string resourceId, int version, string id);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information,
        Message = "Updated activation for resource '{ResourceId}' channel '{Channel}' (Mode={Mode}).")]
    private partial void LogActivationUpdated(string resourceId, string channel, string mode);
}
