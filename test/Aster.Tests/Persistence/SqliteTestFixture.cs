using Aster.Persistence.Sqlite;
using Aster.Persistence.Sqlite.Persistence;
using Aster.Persistence.Sqlite.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Persistence;

/// <summary>
/// Creates a named in-memory Sqlite database with schema initialized.
/// Keeps a holding connection open so the database survives across multiple
/// provider connections (each provider method opens its own connection).
/// Dispose this fixture to tear down the in-memory database.
/// </summary>
internal sealed class SqliteTestFixture : IDisposable
{
    private readonly SqliteConnection holdingConnection;

    public SqlitePersistenceOptions Options { get; }
    public SqliteResourceDefinitionStore DefinitionStore { get; }
    public SqliteResourceWriteStore WriteStore { get; }
    public SqliteResourceQueryService QueryService { get; }

    public SqliteTestFixture(string? testName = null)
    {
        var dbName = testName ?? Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        Options = new SqlitePersistenceOptions { ConnectionString = connectionString };

        // Keep a connection open so the in-memory DB stays alive
        holdingConnection = new SqliteConnection(connectionString);
        holdingConnection.Open();

        // Initialize schema
        var schemaInit = new SchemaInitializer(Options, NullLogger<SchemaInitializer>.Instance);
        schemaInit.EnsureCreated();

        DefinitionStore = new SqliteResourceDefinitionStore(Options, NullLogger<SqliteResourceDefinitionStore>.Instance);
        WriteStore = new SqliteResourceWriteStore(Options, NullLogger<SqliteResourceWriteStore>.Instance);
        QueryService = new SqliteResourceQueryService(Options, NullLogger<SqliteResourceQueryService>.Instance);
    }

    public void Dispose()
    {
        holdingConnection.Dispose();
    }
}
