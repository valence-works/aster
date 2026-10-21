using Microsoft.Data.SqlClient;
using Npgsql;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// A throwaway Postgres or SQL Server database for one test, created on the server named by <c>ASTER_TEST_POSTGRES</c>
/// or <c>ASTER_TEST_SQLSERVER</c> and dropped on dispose. Without the variable the test is skipped, unless
/// <c>ASTER_REQUIRE_POSTGRES=1</c> or <c>ASTER_REQUIRE_SQLSERVER=1</c> makes it fail instead (as in CI): the same
/// convention as the provider tests in Aster.Tests.
/// </summary>
internal sealed class ServerDatabase : IDisposable
{
    private readonly bool postgres;
    private readonly string server;
    private readonly string name = $"aster_agents_{Guid.NewGuid():N}";

    public ServerDatabase(string provider)
    {
        postgres = provider == AgentTestHost.Postgres;
        var (serverVariable, requireVariable) = postgres
            ? ("ASTER_TEST_POSTGRES", "ASTER_REQUIRE_POSTGRES")
            : ("ASTER_TEST_SQLSERVER", "ASTER_REQUIRE_SQLSERVER");

        var configured = Environment.GetEnvironmentVariable(serverVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (Environment.GetEnvironmentVariable(requireVariable) == "1")
                throw new InvalidOperationException($"{requireVariable}=1 but {serverVariable} is not set.");

            Skip.If(true, $"Set {serverVariable} to run the agent store tests on {provider}.");
        }

        server = configured!;
        if (postgres)
        {
            ExecutePostgres(server, $"CREATE DATABASE {name};");
            ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
        }
        else
        {
            ExecuteSqlServer(server, $"CREATE DATABASE [{name}];");
            ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        }
    }

    public string ConnectionString { get; }

    public void Dispose()
    {
        if (postgres)
        {
            NpgsqlConnection.ClearAllPools();
            ExecutePostgres(server, $"DROP DATABASE IF EXISTS {name} WITH (FORCE);");
            return;
        }

        // The database's pooled connections would hold it open; SINGLE_USER ends any others.
        using (var pooled = new SqlConnection(ConnectionString))
            SqlConnection.ClearPool(pooled);
        ExecuteSqlServer(server, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
    }

    private static void ExecutePostgres(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static void ExecuteSqlServer(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        command.ExecuteNonQuery();
    }
}
