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
    private readonly ResourceQueryValidator validator;

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonQueryService"/>.
    /// </summary>
    /// <param name="options">Provider options.</param>
    /// <param name="capabilityProviders">Registered provider capability declarations.</param>
    public SqliteJsonQueryService(
        SqliteJsonAsterOptions options,
        IEnumerable<IResourceQueryCapabilitiesProvider>? capabilityProviders = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        connectionString = options.ConnectionString;
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
        RegisterTextFunctions(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = builder.Build();
        builder.Parameters.ApplyTo(command);

        return await ReadResourcesAsync(command, cancellationToken);
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

    private static void RegisterTextFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, bool>(
            "aster_text_equals",
            (actual, expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase));

        connection.CreateFunction<string?, string?, bool>(
            "aster_text_contains",
            (actual, expected) =>
                actual is not null
                && expected is not null
                && actual.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private void ThrowIfInvalid(ResourceQuery query)
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);
    }

}
