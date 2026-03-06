using System.Diagnostics;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.Sqlite.Internal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aster.Persistence.Sqlite.Persistence;

/// <summary>
/// Sqlite-backed implementation of <see cref="IResourceQueryService"/>.
/// Translates <see cref="ResourceQuery"/> AST to parameterised SQL.
/// </summary>
public sealed partial class SqliteResourceQueryService : IResourceQueryService
{
    private readonly SqlitePersistenceOptions options;
    private readonly ILogger<SqliteResourceQueryService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteResourceQueryService"/> class.
    /// </summary>
    /// <param name="options">Sqlite persistence options.</param>
    /// <param name="logger">Logger instance.</param>
    public SqliteResourceQueryService(
        SqlitePersistenceOptions options,
        ILogger<SqliteResourceQueryService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var translator = new SqliteQueryTranslator();
        var (sql, parameters) = translator.Translate(query);

        var sw = Stopwatch.StartNew();

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        var resources = new List<Resource>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            resources.Add(ReadResource(reader));

        sw.Stop();

        if (sw.ElapsedMilliseconds > options.SlowQueryThresholdMs)
            LogSlowQuery(query.DefinitionId ?? "(all)", sw.ElapsedMilliseconds, options.SlowQueryThresholdMs);

        LogQueryExecuted(query.DefinitionId ?? "(all)", resources.Count);
        return resources;
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

    [LoggerMessage(EventId = 3200, Level = LogLevel.Debug,
        Message = "Query executed for definition '{DefinitionId}', returned {Count} result(s).")]
    private partial void LogQueryExecuted(string definitionId, int count);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Warning,
        Message = "Slow query detected for definition '{DefinitionId}': {ElapsedMs} ms (threshold: {ThresholdMs} ms).")]
    private partial void LogSlowQuery(string definitionId, long elapsedMs, int thresholdMs);
}
