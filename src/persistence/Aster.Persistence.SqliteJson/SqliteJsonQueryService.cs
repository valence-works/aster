using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson.Querying;
using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// SQLite JSON implementation of <see cref="IResourceQueryService"/>.
/// </summary>
public sealed class SqliteJsonQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string connectionString;
    private readonly IResourceLifecycleMarkerStore? markerStore;
    private readonly ResourceQueryValidator validator;

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonQueryService"/>.
    /// </summary>
    /// <param name="options">Provider options.</param>
    /// <param name="capabilityProviders">Registered provider capability declarations.</param>
    /// <param name="markerStore">Optional lifecycle marker store for explicit lifecycle-state filtering.</param>
    public SqliteJsonQueryService(
        SqliteJsonAsterOptions options,
        IEnumerable<IResourceQueryCapabilitiesProvider>? capabilityProviders = null,
        IResourceLifecycleMarkerStore? markerStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        connectionString = options.ConnectionString;
        this.markerStore = markerStore;
        validator = new ResourceQueryValidator(
            capabilityProviders ?? [new SqliteJsonQueryCapabilitiesProvider()],
            this);

        if (options.InitializeSchema)
            SqliteJsonSchema.Initialize(connectionString);
    }

    /// <inheritdoc />
    public string ProviderKey => SqliteJsonQueryCapabilitiesProvider.ProviderKey;

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(query);

        var builder = new SqliteQueryBuilder(query);
        var translator = new SqliteWhereTranslator(builder.Parameters);

        if (query.Filter is not null)
        {
            translator.Validate(query.Filter);
            builder.AddPredicate(translator.Translate(query.Filter));
        }

        builder.AddSorts(query.Sorts);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        RegisterTextBehavior(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = builder.Build();
        builder.Parameters.ApplyTo(command);

        var resources = await ReadResourcesAsync(command, cancellationToken);
        return query.LifecycleState is null
            ? resources
            : await ApplyLifecycleStateFilterAsync(resources, query, cancellationToken);
    }

    private async ValueTask<IEnumerable<Resource>> ApplyLifecycleStateFilterAsync(
        IReadOnlyList<Resource> resources,
        ResourceQuery query,
        CancellationToken cancellationToken)
    {
        if (markerStore is null)
            return resources;

        var tenant = TenantScopeResolver.Resolve(query.TenantScope);
        var markers = await markerStore.GetMarkersAsync(
            resources.Select(static resource => resource.ResourceId).Distinct(StringComparer.Ordinal),
            tenant,
            cancellationToken);
        var expected = query.LifecycleState!.Value;

        return resources.Where(resource =>
        {
            var actual = markers.TryGetValue(resource.ResourceId, out var marker)
                ? marker.State
                : ResourceLifecycleMarkerState.None;
            return actual == expected;
        }).ToList();
    }

    private static async Task<List<Resource>> ReadResourcesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var resources = new List<Resource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            resources.Add(Deserialize(payload));
        }

        return resources;
    }

    private static Resource Deserialize(string payload) =>
        JsonSerializer.Deserialize<Resource>(payload, JsonOptions)
        ?? throw new InvalidOperationException("Unable to deserialize persisted Resource payload.");

    private static void RegisterTextBehavior(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, bool>(
            SqliteTextBehavior.EqualsFunction,
            SqliteTextBehavior.EqualsIgnoreCase);

        connection.CreateFunction<string?, string?, bool>(
            SqliteTextBehavior.ContainsFunction,
            SqliteTextBehavior.ContainsIgnoreCase);

        connection.CreateFunction<string?, string?, bool>(
            SqliteTextBehavior.StartsWithFunction,
            SqliteTextBehavior.StartsWithIgnoreCase);

        connection.CreateFunction<string?, string?>(
            SqliteDateTimeBehavior.DateKeyFunction,
            SqliteDateTimeBehavior.NormalizeDateKey);

        connection.CreateCollation(
            SqliteTextBehavior.OrdinalIgnoreCaseCollation,
            SqliteTextBehavior.CompareIgnoreCase);
    }

    private void ThrowIfInvalid(ResourceQuery query)
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);
    }

}
