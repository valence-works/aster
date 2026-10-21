using Aster.Core.Models.Querying;
using Aster.Persistence.SqlServerJson;
using Aster.Persistence.SqlServerJson.Querying;
using Aster.Tests.Persistence;
using Aster.Tests.PostgresJsonb;
using Microsoft.Data.SqlClient;

namespace Aster.Tests.SqlServerJson;

/// <summary>
/// SQLite normalizes stored date/times by calling <see cref="QueryDateTimeValue.TryNormalizeDateKey"/>; SQL Server
/// runs <see cref="SqlServerDateKey"/>'s function instead. These pin the two to the same keys, over the same edge cases
/// as the Postgres function.
/// </summary>
public sealed class SqlServerJsonDateKeyTests : IDisposable
{
    private readonly SqlServerTestDatabase database = new();

    public void Dispose() => database.Dispose();

    public static TheoryData<string> Values() => PostgresJsonbDateKeyTests.Values();

    [SkippableFact]
    public async Task DateKeyFunction_MatchesQueryDateTimeValue()
    {
        await new SqlServerJsonInfrastructureRunner(database.ConnectionString, SqlServerJsonSchema.Steps).ApplyAsync();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        foreach (var value in Values().Select(static row => (string)row[0]))
        {
            var expected = QueryDateTimeValue.TryNormalizeDateKey(value, out var key) ? key : null;
            await using var command = new SqlCommand($"SELECT {SqlServerDateKey.Of("@value")};", connection);
            command.Parameters.Add(new SqlParameter("@value", System.Data.SqlDbType.NVarChar, -1) { Value = value });
            var actual = await command.ExecuteScalarAsync();

            Assert.True(expected == (actual is DBNull ? null : (string?)actual), $"'{value}': expected {expected ?? "NULL"}, got {actual}.");
        }
    }

    [SkippableFact]
    public async Task DateKeyFunction_IsDeterministic_SoComputedColumnsCanIndexIt()
    {
        await new SqlServerJsonInfrastructureRunner(database.ConnectionString, SqlServerJsonSchema.Steps).ApplyAsync();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT OBJECTPROPERTY(OBJECT_ID(N'{SqlServerDateKey.Function}'), 'IsDeterministic');", connection);

        Assert.Equal(1, await command.ExecuteScalarAsync());
    }
}
