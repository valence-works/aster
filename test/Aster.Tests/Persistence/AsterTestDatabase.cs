using Aster.Core.Extensions;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.SqlServerJson;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Aster.Tests.Persistence;

/// <summary>
/// A persistent database that store tests open providers against. Providers created from the same instance
/// see the same data, so a test can write through one and read through a fresh one. Disposing deletes it.
/// </summary>
public interface IAsterTestDatabase : IDisposable
{
    /// <summary>
    /// Builds a provider with <c>AddAsterCore()</c> and this database's persistence provider.
    /// </summary>
    ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null);
}

/// <summary>
/// A SQLite JSON database in a temporary file.
/// </summary>
public sealed class SqliteTestDatabase : IAsterTestDatabase
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={Path}";

    public ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options => options.ConnectionString = ConnectionString);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(Path);
}

/// <summary>
/// A fresh PostgreSQL database on the server named by <c>ASTER_TEST_POSTGRES</c>, created on first use and
/// dropped on dispose. Without that variable the test is skipped, unless <c>ASTER_REQUIRE_POSTGRES=1</c> (as in
/// CI), where it fails instead so a missing server can't pass silently.
/// </summary>
public sealed class PostgresTestDatabase(Action<PostgresJsonbAsterOptions>? configureOptions = null) : IAsterTestDatabase
{
    public const string ServerVariable = "ASTER_TEST_POSTGRES";
    public const string RequireVariable = "ASTER_REQUIRE_POSTGRES";

    private readonly string name = $"aster_test_{Guid.NewGuid():N}";
    private string? connectionString;

    /// <summary>
    /// The connection string of this test's database, creating it on first use.
    /// </summary>
    public string ConnectionString => connectionString ??= Create();

    public ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddAsterCore()
            .AddAsterPostgresJsonb(options =>
            {
                options.ConnectionString = ConnectionString;
                configureOptions?.Invoke(options);
            });
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (connectionString is null)
            return;

        NpgsqlConnection.ClearAllPools();
        Execute(ServerConnectionString()!, $"DROP DATABASE IF EXISTS {name} WITH (FORCE);");
    }

    /// <summary>
    /// The server connection string, or a skip (or failure, when Postgres is required) without one.
    /// </summary>
    public static string? ServerConnectionString()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrWhiteSpace(server))
            return server;

        if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
            throw new InvalidOperationException($"{RequireVariable}=1 but {ServerVariable} is not set.");

        Skip.If(true, $"Set {ServerVariable} to a PostgreSQL connection string to run the Postgres provider tests.");
        return null;
    }

    private string Create()
    {
        var server = ServerConnectionString()!;
        Execute(server, $"CREATE DATABASE {name};");
        return new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// A fresh SQL Server database on the server named by <c>ASTER_TEST_SQLSERVER</c>, created on first use and
/// dropped on dispose. Without that variable the test is skipped, unless <c>ASTER_REQUIRE_SQLSERVER=1</c> (as in
/// CI), where it fails instead so a missing server can't pass silently.
/// </summary>
public sealed class SqlServerTestDatabase(Action<SqlServerJsonAsterOptions>? configureOptions = null) : IAsterTestDatabase
{
    public const string ServerVariable = "ASTER_TEST_SQLSERVER";
    public const string RequireVariable = "ASTER_REQUIRE_SQLSERVER";

    private readonly string name = $"aster_test_{Guid.NewGuid():N}";
    private string? connectionString;

    /// <summary>
    /// The connection string of this test's database, creating it on first use.
    /// </summary>
    public string ConnectionString => connectionString ??= Create();

    public ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqlServerJson(options =>
            {
                options.ConnectionString = ConnectionString;
                configureOptions?.Invoke(options);
            });
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (connectionString is null)
            return;

        // The database's pooled connections would hold it open; SINGLE_USER ends any others.
        using (var pooled = new SqlConnection(connectionString))
            SqlConnection.ClearPool(pooled);

        Execute(ServerConnectionString()!, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
    }

    /// <summary>
    /// The server connection string, or a skip (or failure, when SQL Server is required) without one.
    /// </summary>
    public static string? ServerConnectionString()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrWhiteSpace(server))
            return server;

        if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
            throw new InvalidOperationException($"{RequireVariable}=1 but {ServerVariable} is not set.");

        Skip.If(true, $"Set {ServerVariable} to a SQL Server connection string to run the SQL Server provider tests.");
        return null;
    }

    private string Create()
    {
        var server = ServerConnectionString()!;
        Execute(server, $"CREATE DATABASE [{name}];");
        return new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        command.ExecuteNonQuery();
    }
}
