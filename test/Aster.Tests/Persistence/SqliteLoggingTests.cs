using Aster.Core.Models.Instances;
using Aster.Persistence.Sqlite;
using Aster.Persistence.Sqlite.Persistence;
using Aster.Persistence.Sqlite.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aster.Tests.Persistence;

/// <summary>
/// ILogger output verification tests covering:
/// - Lifecycle event (Information)
/// - ConcurrencyConflict detection (Warning on slow query threshold)
/// </summary>
public sealed class SqliteLoggingTests : IDisposable
{
    private readonly SqliteConnection holdingConnection;
    private readonly SqlitePersistenceOptions options;

    public SqliteLoggingTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        options = new SqlitePersistenceOptions
        {
            ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared"
        };

        holdingConnection = new SqliteConnection(options.ConnectionString);
        holdingConnection.Open();

        var schema = new SchemaInitializer(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<SchemaInitializer>.Instance);
        schema.EnsureCreated();
    }

    public void Dispose() => holdingConnection.Dispose();

    [Fact]
    public async Task DefinitionStore_RegisterDefinition_LogsInformation()
    {
        var logCapture = new LogCapture<SqliteResourceDefinitionStore>();
        var store = new SqliteResourceDefinitionStore(options, logCapture);

        var def = new Aster.Core.Definitions.ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build();

        await store.RegisterDefinitionAsync(def);

        Assert.Contains(logCapture.Entries, e =>
            e.LogLevel == LogLevel.Information &&
            e.Message.Contains("Registered definition") &&
            e.Message.Contains("Product"));
    }

    [Fact]
    public async Task WriteStore_SaveVersion_LogsInformation()
    {
        var logCapture = new LogCapture<SqliteResourceWriteStore>();
        var store = new SqliteResourceWriteStore(options, logCapture);

        var resource = new Resource
        {
            ResourceId = "log-res",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
        };

        await store.SaveVersionAsync(resource);

        Assert.Contains(logCapture.Entries, e =>
            e.LogLevel == LogLevel.Information &&
            e.Message.Contains("Saved resource") &&
            e.Message.Contains("log-res"));
    }

    [Fact]
    public async Task WriteStore_UpdateActivation_LogsInformation()
    {
        var logCapture = new LogCapture<SqliteResourceWriteStore>();
        var store = new SqliteResourceWriteStore(options, logCapture);

        // First save a resource, then activate
        await store.SaveVersionAsync(new Resource
        {
            ResourceId = "log-act-res",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
        });

        await store.UpdateActivationAsync("log-act-res", "Published", new ActivationState
        {
            ResourceId = "log-act-res",
            Channel = "Published",
            Mode = ChannelMode.SingleActive,
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });

        Assert.Contains(logCapture.Entries, e =>
            e.LogLevel == LogLevel.Information &&
            e.Message.Contains("Updated activation") &&
            e.Message.Contains("Published"));
    }

    [Fact]
    public async Task QueryService_SlowQuery_LogsWarning()
    {
        // Set threshold to 0 ms so any query will be "slow"
        var slowOptions = new SqlitePersistenceOptions
        {
            ConnectionString = options.ConnectionString,
            SlowQueryThresholdMs = -1,
        };

        var logCapture = new LogCapture<SqliteResourceQueryService>();
        var queryService = new SqliteResourceQueryService(slowOptions, logCapture);

        await queryService.QueryAsync(new Aster.Core.Models.Querying.ResourceQuery
        {
            DefinitionId = "Product"
        });

        Assert.Contains(logCapture.Entries, e =>
            e.LogLevel == LogLevel.Warning &&
            e.Message.Contains("Slow query"));
    }

    /// <summary>
    /// Simple ILogger capture for testing log output.
    /// </summary>
    private sealed class LogCapture<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message);
    }
}
