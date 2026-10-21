using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using System.Data.Common;

namespace Aster.Persistence.Relational;

/// <summary>
/// Aster's low-level definition, resource version, activation, lifecycle marker and portability contracts over
/// a relational database with JSON payload columns. A <see cref="RelationalJsonDialect"/> supplies what differs
/// between databases.
/// </summary>
internal abstract class RelationalJsonResourceStore :
    IResourceDefinitionStore,
    IResourceVersionReader,
    IResourceVersionWriter,
    IResourceActivationStateReader,
    IResourceVersionPruningStore,
    IResourcePortabilityStore,
    IResourceLifecycleMarkerClearStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RelationalJsonDialect dialect;
    private readonly int maxParametersPerQuery;
    private readonly string payloadSql;

    /// <summary>
    /// Initializes a new instance of <see cref="RelationalJsonResourceStore"/>.
    /// </summary>
    /// <param name="dialect">The provider's database dialect.</param>
    protected RelationalJsonResourceStore(RelationalJsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        this.dialect = dialect;
        maxParametersPerQuery = dialect.MaxParametersPerQuery;
        payloadSql = dialect.JsonParameter("@payload");
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default) =>
        await GetDefinitionAsync(definitionId, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionAsync(
        string definitionId,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM resource_definitions
            WHERE tenant_id = @tenantId
                AND definition_id = @definitionId
                AND version = (
                    SELECT MAX(version)
                    FROM resource_definitions
                    WHERE tenant_id = @tenantId AND definition_id = @definitionId);
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@definitionId", definitionId);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<ResourceDefinition>(payload);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(
        string definitionId,
        int version,
        CancellationToken cancellationToken = default) =>
        await GetDefinitionVersionAsync(definitionId, version, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(
        string definitionId,
        int version,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM resource_definitions
            WHERE tenant_id = @tenantId AND definition_id = @definitionId AND version = @version;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@definitionId", definitionId);
        Add(command, "@version", version);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<ResourceDefinition>(payload);
    }

    /// <inheritdoc />
    public async ValueTask RegisterDefinitionAsync(
        ResourceDefinition definition,
        CancellationToken cancellationToken = default) =>
        await RegisterDefinitionAsync(definition, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask RegisterDefinitionAsync(
        ResourceDefinition definition,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        await RegisterAsync(definition, expectedLatestVersion: null, tenantScope, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceDefinitionRegistrationResult> RegisterDefinitionIfLatestAsync(
        ResourceDefinition definition,
        int expectedLatestVersion,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLatestVersion);
        return await RegisterAsync(definition, expectedLatestVersion, tenantScope, cancellationToken);
    }

    private async Task<ResourceDefinitionRegistrationResult> RegisterAsync(
        ResourceDefinition definition,
        int? expectedLatestVersion,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        FacetMetadataValidator.Validate(definition);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);

        // The latest-version check and the insert must be atomic against concurrent registrations.
        await using var transaction = await dialect.BeginSerializedWriteAsync(
            connection,
            $"resource_definitions/{tenant.TenantId}/{definition.DefinitionId}",
            cancellationToken);

        var latestVersion = await GetLatestDefinitionVersionAsync(connection, transaction, definition.DefinitionId, tenant, cancellationToken);
        if (expectedLatestVersion is { } expected && expected != latestVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ResourceDefinitionRegistrationResult
            {
                Status = ResourceDefinitionRegistrationStatus.VersionConflict,
                LatestVersion = latestVersion,
            };
        }

        var versionedDefinition = definition with { TenantScope = tenant, Version = latestVersion + 1 };

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO resource_definitions (tenant_id, definition_id, version, id, payload)
            VALUES (@tenantId, @definitionId, @version, @id, {payloadSql});
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@definitionId", versionedDefinition.DefinitionId);
        Add(command, "@version", versionedDefinition.Version);
        Add(command, "@id", versionedDefinition.Id);
        Add(command, "@payload", Serialize(versionedDefinition));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ResourceDefinitionRegistrationResult
        {
            Status = ResourceDefinitionRegistrationStatus.Registered,
            Definition = versionedDefinition,
            LatestVersion = versionedDefinition.Version,
        };
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(
        CancellationToken cancellationToken = default) =>
        await ListDefinitionsAsync(TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rd.payload
            FROM resource_definitions rd
            INNER JOIN (
                SELECT tenant_id, definition_id, MAX(version) AS version
                FROM resource_definitions
                WHERE tenant_id = @tenantId
                GROUP BY tenant_id, definition_id
            ) latest
                ON latest.tenant_id = rd.tenant_id
                AND latest.definition_id = rd.definition_id
                AND latest.version = rd.version
            WHERE rd.tenant_id = @tenantId
            ORDER BY rd.definition_id;
            """;
        Add(command, "@tenantId", tenant.TenantId);

        return await ReadPayloadsAsync<ResourceDefinition>(command, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Resource> SaveVersionAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var tenant = TenantScopeResolver.Resolve(resource.TenantScope);
        var scopedResource = resource with { TenantScope = tenant };

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO resource_versions (
                tenant_id,
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
                @tenantId,
                @resourceId,
                @version,
                @id,
                @definitionId,
                @definitionVersion,
                @created,
                @owner,
                @hash,
                {payloadSql}
            );
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", scopedResource.ResourceId);
        Add(command, "@version", scopedResource.Version);
        Add(command, "@id", scopedResource.Id);
        Add(command, "@definitionId", scopedResource.DefinitionId);
        Add(command, "@definitionVersion", (object?)scopedResource.DefinitionVersion ?? DBNull.Value);
        Add(command, "@created", dialect.TimestampValue(scopedResource.Created));
        Add(command, "@owner", (object?)scopedResource.Owner ?? DBNull.Value);
        Add(command, "@hash", (object?)scopedResource.Hash ?? DBNull.Value);
        Add(command, "@payload", Serialize(scopedResource));

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (DbException exception) when (dialect.IsUniqueViolation(exception))
        {
            // Another writer stored this version between the manager's base-version check and this insert.
            throw scopedResource.Version == 1
                ? new DuplicateResourceIdException($"A resource with ID '{scopedResource.ResourceId}' already exists.", exception)
                : new ConcurrencyException(
                    $"Concurrency conflict on resource '{scopedResource.ResourceId}': version {scopedResource.Version} was saved by another operation.",
                    exception);
        }

        return scopedResource;
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
        var tenant = TenantScopeResolver.Resolve(state.TenantScope);
        var scopedState = state with
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            Channel = channel,
        };

        await using var connection = await OpenConnectionAsync(cancellationToken);

        // Same lock as PruneVersionAsync, so a version can't become active between prune's "not active" check
        // and its delete, nor be pruned between the existence check below and this write.
        await using var transaction = await dialect.BeginSerializedWriteAsync(
            connection,
            ResourceVersionsLockKey(tenant, resourceId),
            cancellationToken);
        foreach (var version in scopedState.ActiveVersions.Distinct())
        {
            if (!await ResourceVersionExistsAsync(connection, transaction, tenant, resourceId, version, cancellationToken))
                throw new VersionNotFoundException(resourceId, version);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = dialect.UpsertPayload(
            "activation_states",
            [("tenant_id", "@tenantId"), ("resource_id", "@resourceId"), ("channel", "@channel")],
            "@payload");
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);
        Add(command, "@channel", channel);
        Add(command, "@payload", Serialize(scopedState));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return scopedState;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ActivationState>> ReadActivationStatesAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var ids = resourceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return [];

        return await ReadPayloadsByTextIdsAsync<ActivationState>(
            tableName: "activation_states",
            columnName: "resource_id",
            tenant: tenant,
            ids: ids,
            orderBy: "resource_id, channel",
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> PruneVersionAsync(
        string resourceId,
        int resourceVersion,
        TenantScope tenantScope,
        ResourceVersionPruneFloor? floor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);

        // The exists, latest, active and floor checks and the delete must not interleave with an activation or
        // another prune of this resource (UpdateActivationAsync takes the same lock). A concurrent save only adds a
        // newer version, which can't break the floor.
        await using var transaction = await dialect.BeginSerializedWriteAsync(
            connection,
            ResourceVersionsLockKey(tenant, resourceId),
            cancellationToken);

        var targetExists = await ResourceVersionExistsAsync(connection, transaction, tenant, resourceId, resourceVersion, cancellationToken);
        if (!targetExists)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var latestVersion = await ReadLatestResourceVersionAsync(connection, transaction, tenant, resourceId, cancellationToken);
        if (latestVersion == resourceVersion)
            throw new InvalidOperationException($"Resource '{resourceId}' version {resourceVersion} is the latest version and cannot be pruned.");

        var isActive = await IsActiveVersionAsync(connection, transaction, tenant, resourceId, resourceVersion, cancellationToken);
        if (isActive)
            throw new InvalidOperationException($"Resource '{resourceId}' version {resourceVersion} is active and cannot be pruned.");

        if (floor is not null)
        {
            var remaining = await CountRemainingVersionsAsync(connection, transaction, tenant, resourceId, resourceVersion, floor.InactiveVersionsOnly, cancellationToken);
            if (remaining < floor.MinimumRemainingVersions)
            {
                throw new ResourceVersionPruneFloorException(
                    $"Pruning resource '{resourceId}' version {resourceVersion} would leave {remaining} {(floor.InactiveVersionsOnly ? "inactive " : "")}version(s); at least {floor.MinimumRemainingVersions} must remain.");
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM resource_versions
            WHERE tenant_id = @tenantId
                AND resource_id = @resourceId
                AND version = @version;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);
        Add(command, "@version", resourceVersion);

        var removed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM lifecycle_markers
            WHERE tenant_id = @tenantId AND resource_id = @resourceId;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<ResourceLifecycleMarker>(payload);
    }

    private async Task<int> CountRemainingVersionsAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantScope tenant,
        string resourceId,
        int resourceVersion,
        bool inactiveVersionsOnly,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var inactive = inactiveVersionsOnly
            ? $"""
                AND NOT EXISTS (
                    SELECT 1
                    FROM activation_states AS activation
                    WHERE activation.tenant_id = rv.tenant_id
                        AND activation.resource_id = rv.resource_id
                        AND {dialect.ActiveVersionsContain("activation", "rv.version")})
            """
            : "";
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM resource_versions AS rv
            WHERE rv.tenant_id = @tenantId
                AND rv.resource_id = @resourceId
                AND rv.version <> @version
            {inactive};
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);
        Add(command, "@version", resourceVersion);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> ResourceVersionExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantScope tenant,
        string resourceId,
        int resourceVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM resource_versions
            WHERE tenant_id = @tenantId
                AND resource_id = @resourceId
                AND version = @version;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);
        Add(command, "@version", resourceVersion);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<int?> ReadLatestResourceVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantScope tenant,
        string resourceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MAX(version)
            FROM resource_versions
            WHERE tenant_id = @tenantId
                AND resource_id = @resourceId;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private async Task<bool> IsActiveVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantScope tenant,
        string resourceId,
        int resourceVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT 1
            FROM activation_states AS activation
            WHERE activation.tenant_id = @tenantId
                AND activation.resource_id = @resourceId
                AND {dialect.ActiveVersionsContain("activation", "@version")};
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);
        Add(command, "@version", resourceVersion);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
        IEnumerable<string> resourceIds,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var ids = resourceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var markers = await ReadPayloadsByTextIdsAsync<ResourceLifecycleMarker>(
            tableName: "lifecycle_markers",
            columnName: "resource_id",
            tenant: tenant,
            ids: ids,
            orderBy: "resource_id",
            cancellationToken);

        return markers.ToDictionary(static marker => marker.ResourceId, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
        ResourceLifecycleMarker marker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker.ResourceId);
        var tenant = TenantScopeResolver.Resolve(marker.TenantScope);
        var scopedMarker = marker with { TenantScope = tenant };

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = dialect.UpsertPayload(
            "lifecycle_markers",
            [("tenant_id", "@tenantId"), ("resource_id", "@resourceId")],
            "@payload");
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", scopedMarker.ResourceId);
        Add(command, "@payload", Serialize(scopedMarker));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return scopedMarker;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ClearMarkerAsync(
        string resourceId,
        TenantScope tenantScope,
        ResourceLifecycleMarkerState expectedState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM lifecycle_markers
            WHERE tenant_id = @tenantId
                AND resource_id = @resourceId
                AND {dialect.MarkerState} = @expectedState;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@resourceId", resourceId);
        Add(command, "@expectedState", (int)expectedState);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
        ResourceVersionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var resourceIds = request.GetResourceIdSelection();

        // A version number with bounded IDs is a primary-key lookup; don't read the histories.
        if (request is { Version: { } version, Scope: ResourceVersionScope.AllVersions } && resourceIds.Values.Count > 0)
        {
            return await ReadVersionsByKeyAsync(
                tenant,
                resourceIds.Values.Select(resourceId => (resourceId, version)),
                cancellationToken);
        }

        var resources = request.Scope switch
        {
            ResourceVersionScope.Latest => await ReadLatestVersionsAsync(tenant, resourceIds, cancellationToken),
            ResourceVersionScope.AllVersions => await ReadAllVersionsAsync(tenant, resourceIds, cancellationToken),
            ResourceVersionScope.Active => await ReadActiveVersionsAsync(tenant, resourceIds, request.ActivationChannel, cancellationToken),
            ResourceVersionScope.Draft => await ReadDraftVersionsAsync(tenant, resourceIds, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unknown resource version scope.")
        };

        return request.Version is { } requested
            ? resources.Where(resource => resource.Version == requested).ToList()
            : resources;
    }

    /// <inheritdoc />
    public async ValueTask<PortableStoreSnapshot> ReadSnapshotAsync(
        PortableStoreReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = TenantScopeResolver.Resolve(request.ExportRequest.TenantScope);
        if (request.ExportRequest.ScopeMode == PortableExportScopeMode.DefinitionsOnly)
        {
            var selectedDefinitions = await SelectDefinitionsAsync(request.ExportRequest, [], cancellationToken);
            return new PortableStoreSnapshot
            {
                AspectDefinitions = await ReadAspectVersionsAsync(PinnedAspectVersions(selectedDefinitions), tenant, cancellationToken),
                Definitions = selectedDefinitions,
            };
        }

        var resources = SelectResourceVersions(
            request.ExportRequest,
            await ReadScopedResourceVersionsAsync(request.ExportRequest, tenant, cancellationToken));
        var definitions = await SelectDefinitionsAsync(request.ExportRequest, resources, cancellationToken);
        var activationStates = await ReadScopedActivationStatesAsync(
            tenant,
            resources.Select(static resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
        var lifecycleMarkers = await ReadScopedLifecycleMarkersAsync(
            tenant,
            resources.Select(static resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
        var (includedActivationStates, skippedActivationEntries) = SelectActivationStates(resources, activationStates);

        return new PortableStoreSnapshot
        {
            AspectDefinitions = await ReadAspectVersionsAsync(PinnedAspectVersions(definitions), tenant, cancellationToken),
            Definitions = definitions,
            Resources = resources,
            ActivationStates = includedActivationStates,
            LifecycleMarkers = lifecycleMarkers,
            SkippedActivationEntries = skippedActivationEntries,
        };
    }

    /// <inheritdoc />
    public async ValueTask<PortableTargetState> ReadTargetStateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var tenant = TenantScopeResolver.Resolve(snapshot.SourceTenantScope);
        var definitions = await ReadTargetDefinitionVersionsAsync(snapshot, tenant, cancellationToken);
        var resources = await ReadSpecificResourceVersionsAsync(
            snapshot.Resources
                .Select(static resource => new ResourceVersionReference
                {
                    ResourceId = resource.ResourceId,
                    Version = resource.Version,
                })
                .ToHashSet(),
            tenant,
            cancellationToken);
        var activationStates = await ReadSpecificActivationStatesAsync(snapshot.ActivationStates, tenant, cancellationToken);
        var lifecycleMarkers = await ReadSpecificLifecycleMarkersAsync(snapshot.LifecycleMarkers, tenant, cancellationToken);
        var aspectDefinitions = await ReadAspectVersionsAsync(
            snapshot.AspectDefinitions
                .Select(static aspect => (aspect.AspectDefinitionId, aspect.Version))
                .Concat(PinnedAspectVersions(snapshot.Definitions)),
            tenant,
            cancellationToken);

        return new PortableTargetState
        {
            AspectDefinitions = aspectDefinitions,
            Definitions = definitions,
            Resources = resources,
            ActivationStates = activationStates,
            LifecycleMarkers = lifecycleMarkers,
        };
    }

    /// <inheritdoc />
    public async ValueTask ApplyImportAsync(
        PortableSnapshot plannedSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plannedSnapshot);
        var tenant = TenantScopeResolver.Resolve(plannedSnapshot.SourceTenantScope);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var aspect in plannedSnapshot.AspectDefinitions)
                await InsertAspectDefinitionAsync(connection, transaction, aspect, tenant, cancellationToken);

            foreach (var definition in plannedSnapshot.Definitions)
                await InsertDefinitionAsync(connection, transaction, definition with { TenantScope = tenant }, cancellationToken);

            foreach (var resource in plannedSnapshot.Resources)
                await InsertResourceAsync(connection, transaction, resource with { TenantScope = tenant }, cancellationToken);

            foreach (var state in plannedSnapshot.ActivationStates)
                await InsertActivationStateAsync(connection, transaction, state with { TenantScope = tenant }, cancellationToken);

            foreach (var marker in plannedSnapshot.LifecycleMarkers)
                await InsertLifecycleMarkerAsync(connection, transaction, marker with { TenantScope = tenant }, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IEnumerable<Resource>> ReadLatestVersionsAsync(
        TenantScope tenant,
        ResourceVersionReadResourceIdSelection resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.IsEmptyBound)
            return [];

        if (resourceIds.Values.Count > 0)
            return await ReadLatestVersionsByResourceIdsAsync(tenant, resourceIds.Values, cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rv.payload
            FROM resource_versions rv
            INNER JOIN (
                SELECT tenant_id, resource_id, MAX(version) AS version
                FROM resource_versions
                WHERE tenant_id = @tenantId
                GROUP BY tenant_id, resource_id
            ) latest
                ON latest.tenant_id = rv.tenant_id
                AND latest.resource_id = rv.resource_id
                AND latest.version = rv.version
            WHERE rv.tenant_id = @tenantId
            ORDER BY rv.resource_id;
            """;
        Add(command, "@tenantId", tenant.TenantId);

        return await ReadPayloadsAsync<Resource>(command, cancellationToken);
    }

    private async Task<IEnumerable<Resource>> ReadAllVersionsAsync(
        TenantScope tenant,
        ResourceVersionReadResourceIdSelection resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.IsEmptyBound)
            return [];

        if (resourceIds.Values.Count > 0)
        {
            return await ReadPayloadsByTextIdsAsync<Resource>(
                tableName: "resource_versions",
                columnName: "resource_id",
                tenant: tenant,
                ids: resourceIds.Values,
                orderBy: "resource_id, version",
                cancellationToken);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM resource_versions
            WHERE tenant_id = @tenantId
            ORDER BY resource_id, version;
            """;
        Add(command, "@tenantId", tenant.TenantId);

        return await ReadPayloadsAsync<Resource>(command, cancellationToken);
    }

    private async Task<IEnumerable<Resource>> ReadLatestVersionsByResourceIdsAsync(
        TenantScope tenant,
        IReadOnlyCollection<string> resourceIds,
        CancellationToken cancellationToken)
    {
        var results = new List<Resource>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in resourceIds.Order(StringComparer.Ordinal).Chunk(maxParametersPerQuery))
        {
            await using var command = connection.CreateCommand();
            var parameterNames = AddTextParameters(command, "resourceId", batch);
            // Drive from the ID list; a join on a grouped subquery lets SQLite (without ANALYZE
            // statistics) walk every row of the tenant instead (#169).
            command.CommandText = LatestVersionsByResourceIdsSql(parameterNames);
            Add(command, "@tenantId", tenant.TenantId);

            results.AddRange(await ReadPayloadsAsync<Resource>(command, cancellationToken));
        }

        return results;
    }

    internal static string LatestVersionsByResourceIdsSql(IEnumerable<string> parameterNames) => $"""
        SELECT rv.payload
        FROM resource_versions rv
        WHERE rv.tenant_id = @tenantId
          AND rv.resource_id IN ({string.Join(", ", parameterNames)})
          AND rv.version = (
              SELECT MAX(v.version)
              FROM resource_versions v
              WHERE v.tenant_id = rv.tenant_id AND v.resource_id = rv.resource_id)
        ORDER BY rv.resource_id;
        """;

    private async Task<List<ResourceDefinition>> SelectDefinitionsAsync(
        PortableSnapshotExportRequest request,
        IReadOnlyCollection<Resource> resources,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var scopedDefinitions = await ReadScopedDefinitionVersionsAsync(request, resources, tenant, cancellationToken);
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
        TenantScope tenant,
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
            tenant: tenant,
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
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var definitionVersions = snapshot.Definitions
            .Select(static definition => (definition.DefinitionId, Version: definition.Version))
            .Concat(snapshot.Resources
                .Where(static resource => resource.DefinitionVersion is not null)
                .Select(static resource => (resource.DefinitionId, Version: resource.DefinitionVersion!.Value)))
            .ToHashSet();

        if (definitionVersions.Count == 0)
            return [];

        var results = new List<ResourceDefinition>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in definitionVersions
            .OrderBy(static reference => reference.DefinitionId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Version)
            .Chunk(maxParametersPerQuery / 2))
        {
            results.AddRange(await ReadPayloadsByCompositeKeysAsync<ResourceDefinition>(
                connection,
                "resource_definitions",
                ("definition_id", "version"),
                tenant,
                batch.Select(static reference => (reference.DefinitionId, (object)reference.Version)).ToList(),
                cancellationToken));
        }

        return results
            .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Version)
            .ToList();
    }

    private async Task<List<ActivationState>> ReadSpecificActivationStatesAsync(
        IReadOnlyCollection<ActivationState> activationStates,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (activationStates.Count == 0)
            return [];

        var results = new List<ActivationState>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in activationStates
            .Select(static state => (state.ResourceId, state.Channel))
            .Distinct()
            .OrderBy(static reference => reference.ResourceId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Channel, StringComparer.Ordinal)
            .Chunk(maxParametersPerQuery / 2))
        {
            results.AddRange(await ReadPayloadsByCompositeKeysAsync<ActivationState>(
                connection,
                "activation_states",
                ("resource_id", "channel"),
                tenant,
                batch.Select(static reference => (reference.ResourceId, (object)reference.Channel)).ToList(),
                cancellationToken));
        }

        return results
            .OrderBy(static state => state.ResourceId, StringComparer.Ordinal)
            .ThenBy(static state => state.Channel, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<ActivationState>> ReadScopedActivationStatesAsync(
        TenantScope tenant,
        IReadOnlyCollection<string> resourceIds,
        CancellationToken cancellationToken) =>
        await ReadPayloadsByTextIdsAsync<ActivationState>(
            tableName: "activation_states",
            columnName: "resource_id",
            tenant: tenant,
            ids: resourceIds,
            orderBy: "resource_id, channel",
            cancellationToken);

    private async Task<List<ResourceLifecycleMarker>> ReadScopedLifecycleMarkersAsync(
        TenantScope tenant,
        IReadOnlyCollection<string> resourceIds,
        CancellationToken cancellationToken) =>
        await ReadPayloadsByTextIdsAsync<ResourceLifecycleMarker>(
            tableName: "lifecycle_markers",
            columnName: "resource_id",
            tenant: tenant,
            ids: resourceIds,
            orderBy: "resource_id",
            cancellationToken);

    private async Task<List<ResourceLifecycleMarker>> ReadSpecificLifecycleMarkersAsync(
        IReadOnlyCollection<ResourceLifecycleMarker> lifecycleMarkers,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (lifecycleMarkers.Count == 0)
            return [];

        return await ReadScopedLifecycleMarkersAsync(
            tenant,
            lifecycleMarkers.Select(static marker => marker.ResourceId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
    }

    private async Task<List<Resource>> ReadScopedResourceVersionsAsync(
        PortableSnapshotExportRequest request,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (request.ScopeMode == PortableExportScopeMode.SelectedResources
            && request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions)
        {
            return await ReadSpecificResourceVersionsAsync(request.SpecificResourceVersions, tenant, cancellationToken);
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
            columnName: columnName,
            tenant: tenant,
            ids: ids,
            orderBy: "resource_id, version",
            cancellationToken);

        return resources
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(static resource => resource.Version)
            .ToList();
    }

    private async Task<List<Resource>> ReadSpecificResourceVersionsAsync(
        IReadOnlyCollection<ResourceVersionReference> references,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
            return [];

        var results = new List<Resource>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        // The caller's set may use any comparer; the VALUES join returns a row per key, so collapse equal keys.
        foreach (var batch in references
            .Select(static reference => (reference.ResourceId, reference.Version))
            .Distinct()
            .OrderBy(static reference => reference.ResourceId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Version)
            .Chunk(maxParametersPerQuery / 2))
        {
            results.AddRange(await ReadPayloadsByCompositeKeysAsync<Resource>(
                connection,
                "resource_versions",
                ("resource_id", "version"),
                tenant,
                batch.Select(static reference => (reference.ResourceId, (object)reference.Version)).ToList(),
                cancellationToken));
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
        DbCommand command,
        string parameterPrefix,
        IEnumerable<string> values)
    {
        var parameterNames = new List<string>();
        var index = 0;

        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            var parameterName = $"@{parameterPrefix}{index++}";
            Add(command, parameterName, value);
            parameterNames.Add(parameterName);
        }

        return parameterNames;
    }

    /// <summary>
    /// Reads the payloads of one batch of composite keys, ordered by the key columns. The keys drive a
    /// primary-key join from a <c>VALUES</c> list: an <c>OR</c> of key predicates makes SQLite, which has
    /// no <c>ANALYZE</c> statistics here, walk the whole tenant once it has more than a few terms (#171).
    /// </summary>
    private async Task<List<T>> ReadPayloadsByCompositeKeysAsync<T>(
        DbConnection connection,
        string tableName,
        (string First, string Second) columns,
        TenantScope tenant,
        IReadOnlyList<(string First, object Second)> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
            return [];

        await using var command = connection.CreateCommand();
        for (var index = 0; index < keys.Count; index++)
        {
            Add(command, $"@keyFirst{index}", keys[index].First);
            Add(command, $"@keySecond{index}", keys[index].Second);
        }

        command.CommandText = CompositeKeyPayloadsSql(tableName, columns, keys.Count, dialect.KeyRows);
        Add(command, "@tenantId", tenant.TenantId);
        return await ReadPayloadsAsync<T>(command, cancellationToken);
    }

    internal static string CompositeKeyPayloadsSql(string tableName, (string First, string Second) columns, int keyCount) =>
        CompositeKeyPayloadsSql(tableName, columns, keyCount, static rows => $"VALUES {string.Join(", ", rows)}");

    internal static string CompositeKeyPayloadsSql(
        string tableName,
        (string First, string Second) columns,
        int keyCount,
        Func<IEnumerable<string>, string> keyRows) => $"""
        WITH keys(first_key, second_key) AS (
            {keyRows(Enumerable.Range(0, keyCount).Select(static index => $"(@keyFirst{index}, @keySecond{index})"))})
        SELECT t.payload
        FROM keys CROSS JOIN {tableName} t
        WHERE t.tenant_id = @tenantId AND t.{columns.First} = keys.first_key AND t.{columns.Second} = keys.second_key
        ORDER BY t.{columns.First}, t.{columns.Second};
        """;

    private async Task<List<T>> ReadPayloadsByTextIdsAsync<T>(
        string tableName,
        string columnName,
        TenantScope tenant,
        IReadOnlyCollection<string> ids,
        string orderBy,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        if (ids.Count == 0)
            return results;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in ids.Order(StringComparer.Ordinal).Chunk(maxParametersPerQuery))
        {
            await using var command = connection.CreateCommand();
            var parameterNames = AddTextParameters(command, "id", batch);
            command.CommandText = $"""
                SELECT payload
                FROM {tableName}
                WHERE tenant_id = @tenantId AND {columnName} IN ({string.Join(", ", parameterNames)})
                ORDER BY {orderBy};
                """;
            Add(command, "@tenantId", tenant.TenantId);

            results.AddRange(await ReadPayloadsAsync<T>(command, cancellationToken));
        }

        return results;
    }

    private async Task<IEnumerable<Resource>> ReadActiveVersionsAsync(
        TenantScope tenant,
        ResourceVersionReadResourceIdSelection resourceIds,
        string? channel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        // Activation rows are small; read them first and then only the active version rows, rather
        // than every version of every resource in scope.
        var active = await ReadActivationStatesAsync(tenant, resourceIds, channel, cancellationToken);

        return await ReadVersionsByKeyAsync(
            tenant,
            active.SelectMany(static pair => pair.Value.Select(version => (pair.Key, version))),
            cancellationToken);
    }

    /// <summary>
    /// Reads the version rows for the given <c>(resource_id, version)</c> keys, ordered by resource ID and
    /// version. Keys without a row (pruned or never written) are skipped.
    /// </summary>
    private async Task<List<Resource>> ReadVersionsByKeyAsync(
        TenantScope tenant,
        IEnumerable<(string ResourceId, int Version)> keys,
        CancellationToken cancellationToken)
    {
        var results = new List<Resource>();
        var ordered = keys
            .Distinct()
            .OrderBy(static key => key.ResourceId, StringComparer.Ordinal)
            .ThenBy(static key => key.Version)
            .ToList();
        if (ordered.Count == 0)
            return results;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in ordered.Chunk(maxParametersPerQuery / 2))
        {
            results.AddRange(await ReadPayloadsByCompositeKeysAsync<Resource>(
                connection,
                "resource_versions",
                ("resource_id", "version"),
                tenant,
                batch.Select(static key => (key.ResourceId, (object)key.Version)).ToList(),
                cancellationToken));
        }

        return results;
    }

    private async Task<IEnumerable<Resource>> ReadDraftVersionsAsync(
        TenantScope tenant,
        ResourceVersionReadResourceIdSelection resourceIds,
        CancellationToken cancellationToken)
    {
        var resources = (await ReadAllVersionsAsync(tenant, resourceIds, cancellationToken)).ToList();
        var active = await ReadActivationStatesAsync(tenant, resourceIds, channel: null, cancellationToken);

        return resources
            .Where(resource =>
                !active.TryGetValue(resource.ResourceId, out var versions)
                || !versions.Contains(resource.Version))
            .ToList();
    }

    private async Task<Dictionary<string, HashSet<int>>> ReadActivationStatesAsync(
        TenantScope tenant,
        ResourceVersionReadResourceIdSelection resourceIds,
        string? channel,
        CancellationToken cancellationToken)
    {
        List<ActivationState> states;
        if (resourceIds.IsEmptyBound)
        {
            states = [];
        }
        else if (resourceIds.Values.Count > 0)
        {
            states = await ReadPayloadsByTextIdsAsync<ActivationState>(
                tableName: "activation_states",
                columnName: "resource_id",
                tenant: tenant,
                ids: resourceIds.Values,
                orderBy: "resource_id, channel",
                cancellationToken);
        }
        else
        {
            states = await ReadActivationStatesForTenantAsync(tenant, channel, cancellationToken);
        }

        if (resourceIds.Values.Count > 0 && channel is not null)
            states = states.Where(state => string.Equals(state.Channel, channel, StringComparison.Ordinal)).ToList();

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

    private async Task<List<ActivationState>> ReadActivationStatesForTenantAsync(
        TenantScope tenant,
        string? channel,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = channel is null
            ? "SELECT payload FROM activation_states WHERE tenant_id = @tenantId;"
            : "SELECT payload FROM activation_states WHERE tenant_id = @tenantId AND channel = @channel;";

        Add(command, "@tenantId", tenant.TenantId);
        if (channel is not null)
            Add(command, "@channel", channel);

        return await ReadPayloadsAsync<ActivationState>(command, cancellationToken);
    }

    private static IEnumerable<(string AspectDefinitionId, int Version)> PinnedAspectVersions(
        IEnumerable<ResourceDefinition> definitions) =>
        definitions
            .SelectMany(static definition => definition.AspectDefinitions.Values)
            .Where(static aspect => aspect is { IsShared: true })
            .Select(static aspect => (aspect.AspectDefinitionId, aspect.Version));

    private async Task<List<AspectDefinition>> ReadAspectVersionsAsync(
        IEnumerable<(string AspectDefinitionId, int Version)> versions,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var references = versions.ToHashSet();
        if (references.Count == 0)
            return [];

        var results = new List<AspectDefinition>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var batch in references
            .OrderBy(static reference => reference.AspectDefinitionId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Version)
            .Chunk(maxParametersPerQuery / 2))
        {
            results.AddRange(await ReadPayloadsByCompositeKeysAsync<AspectDefinition>(
                connection,
                "aspect_definitions",
                ("aspect_definition_id", "version"),
                tenant,
                batch.Select(static reference => (reference.AspectDefinitionId, (object)reference.Version)).ToList(),
                cancellationToken));
        }

        return results
            .OrderBy(static aspect => aspect.AspectDefinitionId, StringComparer.Ordinal)
            .ThenBy(static aspect => aspect.Version)
            .ToList();
    }

    private async Task InsertAspectDefinitionAsync(
        DbConnection connection,
        DbTransaction transaction,
        AspectDefinition aspect,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var imported = aspect with { IsShared = false };
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO aspect_definitions (tenant_id, aspect_definition_id, version, id, payload)
            VALUES (@tenantId, @aspectDefinitionId, @version, @id, {payloadSql});
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@aspectDefinitionId", imported.AspectDefinitionId);
        Add(command, "@version", imported.Version);
        Add(command, "@id", imported.Id);
        Add(command, "@payload", Serialize(imported));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertDefinitionAsync(
        DbConnection connection,
        DbTransaction transaction,
        ResourceDefinition definition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO resource_definitions (tenant_id, definition_id, version, id, payload)
            VALUES (@tenantId, @definitionId, @version, @id, {payloadSql});
            """;
        Add(command, "@tenantId", TenantScopeResolver.Resolve(definition.TenantScope).TenantId);
        Add(command, "@definitionId", definition.DefinitionId);
        Add(command, "@version", definition.Version);
        Add(command, "@id", definition.Id);
        Add(command, "@payload", Serialize(definition));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertResourceAsync(
        DbConnection connection,
        DbTransaction transaction,
        Resource resource,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO resource_versions (
                tenant_id,
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
                @tenantId,
                @resourceId,
                @version,
                @id,
                @definitionId,
                @definitionVersion,
                @created,
                @owner,
                @hash,
                {payloadSql}
            );
            """;
        Add(command, "@tenantId", TenantScopeResolver.Resolve(resource.TenantScope).TenantId);
        Add(command, "@resourceId", resource.ResourceId);
        Add(command, "@version", resource.Version);
        Add(command, "@id", resource.Id);
        Add(command, "@definitionId", resource.DefinitionId);
        Add(command, "@definitionVersion", (object?)resource.DefinitionVersion ?? DBNull.Value);
        Add(command, "@created", dialect.TimestampValue(resource.Created));
        Add(command, "@owner", (object?)resource.Owner ?? DBNull.Value);
        Add(command, "@hash", (object?)resource.Hash ?? DBNull.Value);
        Add(command, "@payload", Serialize(resource));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertActivationStateAsync(
        DbConnection connection,
        DbTransaction transaction,
        ActivationState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO activation_states (tenant_id, resource_id, channel, payload)
            VALUES (@tenantId, @resourceId, @channel, {payloadSql});
            """;
        Add(command, "@tenantId", TenantScopeResolver.Resolve(state.TenantScope).TenantId);
        Add(command, "@resourceId", state.ResourceId);
        Add(command, "@channel", state.Channel);
        Add(command, "@payload", Serialize(state));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertLifecycleMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        ResourceLifecycleMarker marker,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = dialect.UpsertPayload(
            "lifecycle_markers",
            [("tenant_id", "@tenantId"), ("resource_id", "@resourceId")],
            "@payload");
        Add(command, "@tenantId", TenantScopeResolver.Resolve(marker.TenantScope).TenantId);
        Add(command, "@resourceId", marker.ResourceId);
        Add(command, "@payload", Serialize(marker));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResourceVersionsLockKey(TenantScope tenant, string resourceId) =>
        $"resource_versions/{tenant.TenantId}/{resourceId}";

    private ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        dialect.OpenConnectionAsync(cancellationToken);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async Task<int> GetLatestDefinitionVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string definitionId,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(version), 0)
            FROM resource_definitions
            WHERE tenant_id = @tenantId AND definition_id = @definitionId;
            """;
        Add(command, "@tenantId", tenant.TenantId);
        Add(command, "@definitionId", definitionId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<List<T>> ReadPayloadsAsync<T>(
        DbCommand command,
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
