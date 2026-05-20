using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// SQLite JSON-backed implementation of Aster's low-level definition and resource version contracts.
/// </summary>
public sealed class SqliteJsonResourceStore :
    IResourceDefinitionStore,
    IResourceVersionReader,
    IResourceVersionWriter,
    IResourcePortabilityStore
{
    private const int MaxSqliteParametersPerQuery = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonResourceStore"/>.
    /// </summary>
    /// <param name="options">Provider options.</param>
    public SqliteJsonResourceStore(SqliteJsonAsterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        connectionString = options.ConnectionString;

        if (options.InitializeSchema)
            SqliteJsonSchema.Initialize(connectionString);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM resource_definitions
            WHERE definition_id = $definitionId
            ORDER BY version DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<ResourceDefinition>(payload);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(
        string definitionId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM resource_definitions
            WHERE definition_id = $definitionId AND version = $version;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);
        command.Parameters.AddWithValue("$version", version);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<ResourceDefinition>(payload);
    }

    /// <inheritdoc />
    public async ValueTask RegisterDefinitionAsync(
        ResourceDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var nextVersion = await GetNextDefinitionVersionAsync(connection, definition.DefinitionId, cancellationToken);
        var versionedDefinition = definition with { Version = nextVersion };

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO resource_definitions (definition_id, version, id, payload)
            VALUES ($definitionId, $version, $id, $payload);
            """;
        command.Parameters.AddWithValue("$definitionId", versionedDefinition.DefinitionId);
        command.Parameters.AddWithValue("$version", versionedDefinition.Version);
        command.Parameters.AddWithValue("$id", versionedDefinition.Id);
        command.Parameters.AddWithValue("$payload", Serialize(versionedDefinition));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rd.payload
            FROM resource_definitions rd
            INNER JOIN (
                SELECT definition_id, MAX(version) AS version
                FROM resource_definitions
                GROUP BY definition_id
            ) latest
                ON latest.definition_id = rd.definition_id
                AND latest.version = rd.version
            ORDER BY rd.definition_id;
            """;

        return await ReadPayloadsAsync<ResourceDefinition>(command, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Resource> SaveVersionAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        await using var connection = await OpenConnectionAsync(cancellationToken);
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
        command.Parameters.AddWithValue("$definitionVersion", (object?)resource.DefinitionVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", resource.Created.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$owner", (object?)resource.Owner ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", (object?)resource.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", Serialize(resource));

        await command.ExecuteNonQueryAsync(cancellationToken);
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

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activation_states (resource_id, channel, payload)
            VALUES ($resourceId, $channel, $payload)
            ON CONFLICT(resource_id, channel)
            DO UPDATE SET payload = excluded.payload;
            """;
        command.Parameters.AddWithValue("$resourceId", resourceId);
        command.Parameters.AddWithValue("$channel", channel);
        command.Parameters.AddWithValue("$payload", Serialize(state));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return state;
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
        ResourceVersionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Scope switch
        {
            ResourceVersionScope.Latest => await ReadLatestVersionsAsync(cancellationToken),
            ResourceVersionScope.AllVersions => await ReadAllVersionsAsync(cancellationToken),
            ResourceVersionScope.Active => await ReadActiveVersionsAsync(request.ActivationChannel, cancellationToken),
            ResourceVersionScope.Draft => await ReadDraftVersionsAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unknown resource version scope.")
        };
    }

    /// <inheritdoc />
    public async ValueTask<PortableStoreSnapshot> ReadSnapshotAsync(
        PortableStoreReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExportRequest.ScopeMode == PortableExportScopeMode.DefinitionsOnly)
        {
            return new PortableStoreSnapshot
            {
                Definitions = await SelectDefinitionsAsync(request.ExportRequest, [], cancellationToken),
            };
        }

        var resources = SelectResourceVersions(
            request.ExportRequest,
            await ReadScopedResourceVersionsAsync(request.ExportRequest, cancellationToken));
        var definitions = await SelectDefinitionsAsync(request.ExportRequest, resources, cancellationToken);
        var activationStates = await ReadScopedActivationStatesAsync(
            resources.Select(static resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
        var (includedActivationStates, skippedActivationEntries) = SelectActivationStates(resources, activationStates);

        return new PortableStoreSnapshot
        {
            Definitions = definitions,
            Resources = resources,
            ActivationStates = includedActivationStates,
            SkippedActivationEntries = skippedActivationEntries,
        };
    }

    /// <inheritdoc />
    public async ValueTask<PortableTargetState> ReadTargetStateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var definitions = await ReadTargetDefinitionVersionsAsync(snapshot, cancellationToken);
        var resources = await ReadSpecificResourceVersionsAsync(
            snapshot.Resources
                .Select(static resource => new ResourceVersionReference
                {
                    ResourceId = resource.ResourceId,
                    Version = resource.Version,
                })
                .ToHashSet(),
            cancellationToken);
        var activationStates = await ReadScopedActivationStatesAsync(
            snapshot.ActivationStates
                .Select(static state => state.ResourceId)
                .ToHashSet(StringComparer.Ordinal),
            cancellationToken);
        var activationKeys = snapshot.ActivationStates
            .Select(static state => (state.ResourceId, state.Channel))
            .ToHashSet();

        return new PortableTargetState
        {
            Definitions = definitions,
            Resources = resources,
            ActivationStates = activationStates
                .Where(state => activationKeys.Contains((state.ResourceId, state.Channel)))
                .ToList(),
        };
    }

    /// <inheritdoc />
    public async ValueTask ApplyImportAsync(
        PortableSnapshot plannedSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plannedSnapshot);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var definition in plannedSnapshot.Definitions)
                await InsertDefinitionAsync(connection, transaction, definition, cancellationToken);

            foreach (var resource in plannedSnapshot.Resources)
                await InsertResourceAsync(connection, transaction, resource, cancellationToken);

            foreach (var state in plannedSnapshot.ActivationStates)
                await InsertActivationStateAsync(connection, transaction, state, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IEnumerable<Resource>> ReadLatestVersionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rv.payload
            FROM resource_versions rv
            INNER JOIN (
                SELECT resource_id, MAX(version) AS version
                FROM resource_versions
                GROUP BY resource_id
            ) latest
                ON latest.resource_id = rv.resource_id
                AND latest.version = rv.version
            ORDER BY rv.resource_id;
            """;

        return await ReadPayloadsAsync<Resource>(command, cancellationToken);
    }

    private async Task<IEnumerable<Resource>> ReadAllVersionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM resource_versions
            ORDER BY resource_id, version;
            """;

        return await ReadPayloadsAsync<Resource>(command, cancellationToken);
    }

    private async Task<List<ResourceDefinition>> SelectDefinitionsAsync(
        PortableSnapshotExportRequest request,
        IReadOnlyCollection<Resource> resources,
        CancellationToken cancellationToken)
    {
        var scopedDefinitions = await ReadScopedDefinitionVersionsAsync(request, resources, cancellationToken);
        var scopedDefinitionsByVersion = scopedDefinitions.ToDictionary(
            static definition => (definition.DefinitionId, definition.Version));
        var definitions = new Dictionary<(string DefinitionId, int Version), ResourceDefinition>();

        if (request.ScopeMode is PortableExportScopeMode.DefinitionsOnly or PortableExportScopeMode.DefinitionWithResources)
        {
            foreach (var definition in scopedDefinitions.Where(definition => request.DefinitionIds.Contains(definition.DefinitionId)))
                definitions[(definition.DefinitionId, definition.Version)] = definition;
        }

        foreach (var resource in resources)
        {
            if (resource.DefinitionVersion is null)
                continue;

            if (scopedDefinitionsByVersion.TryGetValue((resource.DefinitionId, resource.DefinitionVersion.Value), out var definition))
                definitions[(definition.DefinitionId, definition.Version)] = definition;
        }

        return definitions.Values
            .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Version)
            .ToList();
    }

    private async Task<List<ResourceDefinition>> ReadScopedDefinitionVersionsAsync(
        PortableSnapshotExportRequest request,
        IReadOnlyCollection<Resource> resources,
        CancellationToken cancellationToken)
    {
        var fullDefinitionIds = request.ScopeMode is PortableExportScopeMode.DefinitionsOnly or PortableExportScopeMode.DefinitionWithResources
            ? request.DefinitionIds
            : [];
        var referencedDefinitionVersions = resources
            .Where(static resource => resource.DefinitionVersion is not null)
            .Select(static resource => (resource.DefinitionId, Version: resource.DefinitionVersion!.Value))
            .ToHashSet();
        var definitionIds = fullDefinitionIds
            .Concat(referencedDefinitionVersions.Select(static reference => reference.DefinitionId))
            .ToHashSet(StringComparer.Ordinal);

        var definitions = await ReadPayloadsByTextIdsAsync<ResourceDefinition>(
            tableName: "resource_definitions",
            columnName: "definition_id",
            ids: definitionIds,
            orderBy: "definition_id, version",
            cancellationToken);

        return definitions
            .Where(definition =>
                fullDefinitionIds.Contains(definition.DefinitionId)
                || referencedDefinitionVersions.Contains((definition.DefinitionId, definition.Version)))
            .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Version)
            .ToList();
    }

    private async Task<List<ResourceDefinition>> ReadTargetDefinitionVersionsAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var definitionIds = snapshot.Definitions
            .Select(static definition => definition.DefinitionId)
            .Concat(snapshot.Resources.Select(static resource => resource.DefinitionId))
            .ToHashSet(StringComparer.Ordinal);
        var definitionVersions = snapshot.Definitions
            .Select(static definition => (definition.DefinitionId, definition.Version))
            .Concat(snapshot.Resources
                .Where(static resource => resource.DefinitionVersion is not null)
                .Select(static resource => (resource.DefinitionId, resource.DefinitionVersion!.Value)))
            .ToHashSet();

        var definitions = await ReadPayloadsByTextIdsAsync<ResourceDefinition>(
            tableName: "resource_definitions",
            columnName: "definition_id",
            ids: definitionIds,
            orderBy: "definition_id, version",
            cancellationToken);

        return definitions
            .Where(definition => definitionVersions.Contains((definition.DefinitionId, definition.Version)))
            .ToList();
    }

    private async Task<List<ActivationState>> ReadScopedActivationStatesAsync(
        IReadOnlyCollection<string> resourceIds,
        CancellationToken cancellationToken) =>
        await ReadPayloadsByTextIdsAsync<ActivationState>(
            tableName: "activation_states",
            columnName: "resource_id",
            ids: resourceIds,
            orderBy: "resource_id, channel",
            cancellationToken);

    private async Task<List<Resource>> ReadScopedResourceVersionsAsync(
        PortableSnapshotExportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ScopeMode == PortableExportScopeMode.SelectedResources
            && request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions)
        {
            return await ReadSpecificResourceVersionsAsync(request.SpecificResourceVersions, cancellationToken);
        }

        var (columnName, ids) = request.ScopeMode switch
        {
            PortableExportScopeMode.DefinitionWithResources => ("definition_id", request.DefinitionIds),
            PortableExportScopeMode.SelectedResources => ("resource_id", request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions
                ? request.SpecificResourceVersions.Select(static reference => reference.ResourceId).ToHashSet(StringComparer.Ordinal)
                : request.ResourceIds),
            _ => ("resource_id", []),
        };

        if (ids.Count == 0)
            return [];

        var resources = await ReadPayloadsByTextIdsAsync<Resource>(
            tableName: "resource_versions",
            columnName,
            ids,
            orderBy: "resource_id, version",
            cancellationToken);

        return resources
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(static resource => resource.Version)
            .ToList();
    }

    private async Task<List<Resource>> ReadSpecificResourceVersionsAsync(
        IReadOnlyCollection<ResourceVersionReference> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
            return [];

        var results = new List<Resource>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in references
            .OrderBy(static reference => reference.ResourceId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Version)
            .Chunk(MaxSqliteParametersPerQuery / 2))
        {
            await using var command = connection.CreateCommand();
            var predicates = AddResourceVersionPredicates(command, batch);
            command.CommandText = $"""
                SELECT payload
                FROM resource_versions
                WHERE {string.Join(" OR ", predicates)}
                ORDER BY resource_id, version;
                """;

            results.AddRange(await ReadPayloadsAsync<Resource>(command, cancellationToken));
        }

        return results
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(static resource => resource.Version)
            .ToList();
    }

    private static List<Resource> SelectResourceVersions(
        PortableSnapshotExportRequest request,
        IReadOnlyCollection<Resource> scopedResources)
    {
        var resourceIds = request.ScopeMode switch
        {
            PortableExportScopeMode.DefinitionsOnly => [],
            PortableExportScopeMode.SelectedResources => request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions
                ? request.SpecificResourceVersions.Select(static reference => reference.ResourceId).ToHashSet(StringComparer.Ordinal)
                : request.ResourceIds,
            PortableExportScopeMode.DefinitionWithResources => scopedResources
                .Where(resource => request.DefinitionIds.Contains(resource.DefinitionId))
                .Select(static resource => resource.ResourceId)
                .ToHashSet(StringComparer.Ordinal),
            _ => [],
        };

        var specificVersions = request.SpecificResourceVersions.ToHashSet();

        return scopedResources
            .Where(resource => resourceIds.Contains(resource.ResourceId))
            .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .SelectMany(group => SelectVersions(
                group.OrderBy(static resource => resource.Version).ToList(),
                request.ResourceVersionScope,
                specificVersions))
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(static resource => resource.Version)
            .ToList();
    }

    private static (List<ActivationState> ActivationStates, List<SkippedActivationEntry> SkippedActivationEntries) SelectActivationStates(
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ActivationState> activationStates)
    {
        var includedVersions = resources
            .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static resource => resource.Version).ToHashSet(),
                StringComparer.Ordinal);

        var includedStates = new List<ActivationState>();
        var skippedEntries = new List<SkippedActivationEntry>();

        foreach (var state in activationStates)
        {
            if (!includedVersions.TryGetValue(state.ResourceId, out var includedResourceVersions))
                continue;

            var includedActiveVersions = state.ActiveVersions
                .Where(includedResourceVersions.Contains)
                .Order()
                .ToList();

            foreach (var skippedVersion in state.ActiveVersions.Except(includedActiveVersions).Order())
            {
                skippedEntries.Add(new SkippedActivationEntry
                {
                    ResourceId = state.ResourceId,
                    Channel = state.Channel,
                    Version = skippedVersion,
                    Reason = SkippedActivationReason.ExcludedByResourceVersionScope,
                });
            }

            if (includedActiveVersions.Count == 0)
                continue;

            includedStates.Add(state with { ActiveVersions = includedActiveVersions });
        }

        return (includedStates, skippedEntries);
    }

    private static IEnumerable<Resource> SelectVersions(
        IReadOnlyList<Resource> versions,
        PortableResourceVersionScope versionScope,
        IReadOnlySet<ResourceVersionReference> specificVersions) =>
        versionScope switch
        {
            PortableResourceVersionScope.AllVersions => versions,
            PortableResourceVersionScope.LatestOnly => versions.Count > 0 ? [versions[^1]] : [],
            PortableResourceVersionScope.SpecificVersions => versions
                .Where(version => specificVersions.Contains(new ResourceVersionReference
                {
                    ResourceId = version.ResourceId,
                    Version = version.Version,
                })),
            _ => [],
        };

    private static List<string> AddTextParameters(
        SqliteCommand command,
        string parameterPrefix,
        IEnumerable<string> values)
    {
        var parameterNames = new List<string>();
        var index = 0;

        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            var parameterName = $"${parameterPrefix}{index++}";
            command.Parameters.AddWithValue(parameterName, value);
            parameterNames.Add(parameterName);
        }

        return parameterNames;
    }

    private static List<string> AddResourceVersionPredicates(
        SqliteCommand command,
        IReadOnlyList<ResourceVersionReference> references)
    {
        var predicates = new List<string>();

        for (var index = 0; index < references.Count; index++)
        {
            var resourceIdParameter = $"$resourceId{index}";
            var versionParameter = $"$version{index}";
            command.Parameters.AddWithValue(resourceIdParameter, references[index].ResourceId);
            command.Parameters.AddWithValue(versionParameter, references[index].Version);
            predicates.Add($"(resource_id = {resourceIdParameter} AND version = {versionParameter})");
        }

        return predicates;
    }

    private async Task<List<T>> ReadPayloadsByTextIdsAsync<T>(
        string tableName,
        string columnName,
        IReadOnlyCollection<string> ids,
        string orderBy,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        if (ids.Count == 0)
            return results;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in ids.Order(StringComparer.Ordinal).Chunk(MaxSqliteParametersPerQuery))
        {
            await using var command = connection.CreateCommand();
            var parameterNames = AddTextParameters(command, "id", batch);
            command.CommandText = $"""
                SELECT payload
                FROM {tableName}
                WHERE {columnName} IN ({string.Join(", ", parameterNames)})
                ORDER BY {orderBy};
                """;

            results.AddRange(await ReadPayloadsAsync<T>(command, cancellationToken));
        }

        return results;
    }

    private async Task<IEnumerable<Resource>> ReadActiveVersionsAsync(
        string? channel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var resources = (await ReadAllVersionsAsync(cancellationToken)).ToList();
        var active = await ReadActivationStatesAsync(channel, cancellationToken);

        return resources
            .Where(resource =>
                active.TryGetValue(resource.ResourceId, out var versions)
                && versions.Contains(resource.Version))
            .ToList();
    }

    private async Task<IEnumerable<Resource>> ReadDraftVersionsAsync(CancellationToken cancellationToken)
    {
        var resources = (await ReadAllVersionsAsync(cancellationToken)).ToList();
        var active = await ReadActivationStatesAsync(channel: null, cancellationToken);

        return resources
            .Where(resource =>
                !active.TryGetValue(resource.ResourceId, out var versions)
                || !versions.Contains(resource.Version))
            .ToList();
    }

    private async Task<Dictionary<string, HashSet<int>>> ReadActivationStatesAsync(
        string? channel,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = channel is null
            ? "SELECT payload FROM activation_states;"
            : "SELECT payload FROM activation_states WHERE channel = $channel;";

        if (channel is not null)
            command.Parameters.AddWithValue("$channel", channel);

        var states = await ReadPayloadsAsync<ActivationState>(command, cancellationToken);
        var active = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var state in states)
        {
            if (!active.TryGetValue(state.ResourceId, out var versions))
            {
                versions = [];
                active[state.ResourceId] = versions;
            }

            foreach (var version in state.ActiveVersions)
                versions.Add(version);
        }

        return active;
    }

    private async Task InsertDefinitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ResourceDefinition definition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO resource_definitions (definition_id, version, id, payload)
            VALUES ($definitionId, $version, $id, $payload);
            """;
        command.Parameters.AddWithValue("$definitionId", definition.DefinitionId);
        command.Parameters.AddWithValue("$version", definition.Version);
        command.Parameters.AddWithValue("$id", definition.Id);
        command.Parameters.AddWithValue("$payload", Serialize(definition));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertResourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Resource resource,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        command.Parameters.AddWithValue("$definitionVersion", (object?)resource.DefinitionVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", resource.Created.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$owner", (object?)resource.Owner ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", (object?)resource.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", Serialize(resource));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertActivationStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ActivationState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activation_states (resource_id, channel, payload)
            VALUES ($resourceId, $channel, $payload);
            """;
        command.Parameters.AddWithValue("$resourceId", state.ResourceId);
        command.Parameters.AddWithValue("$channel", state.Channel);
        command.Parameters.AddWithValue("$payload", Serialize(state));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<int> GetNextDefinitionVersionAsync(
        SqliteConnection connection,
        string definitionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(version), 0) + 1
            FROM resource_definitions
            WHERE definition_id = $definitionId;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<List<T>> ReadPayloadsAsync<T>(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            results.Add(Deserialize<T>(payload));
        }

        return results;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions)
        ?? throw new InvalidOperationException($"Unable to deserialize persisted {typeof(T).Name} payload.");
}
