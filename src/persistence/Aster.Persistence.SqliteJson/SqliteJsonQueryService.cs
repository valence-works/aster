using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqliteJson.Querying;
using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// SQLite JSON implementation of <see cref="IResourceQueryService"/>.
/// </summary>
public sealed class SqliteJsonQueryService : IResourceQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string connectionString;

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteJsonQueryService"/>.
    /// </summary>
    /// <param name="options">Provider options.</param>
    public SqliteJsonQueryService(SqliteJsonAsterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        connectionString = options.ConnectionString;
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

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

        await using var command = connection.CreateCommand();
        command.CommandText = builder.Build();
        builder.Parameters.ApplyTo(command);

        return await ReadResourcesAsync(command, cancellationToken);
    }

    private static void Validate(ResourceQuery query)
    {
        if (!Enum.IsDefined(query.Scope))
            throw Unsupported($"Resource version scope '{query.Scope}'");

        if (query.Scope == ResourceVersionScope.Active && string.IsNullOrWhiteSpace(query.ActivationChannel))
            throw Unsupported("Active scope without an activation channel");

        if (query.Skip is < 0)
            throw Unsupported("Negative Skip");

        if (query.Take is < 0)
            throw Unsupported("Negative Take");
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

    private static UnsupportedQueryFeatureException Unsupported(string feature) =>
        new($"{feature} is not supported by the SQLite JSON query provider.");
}
