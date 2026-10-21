using Aster.Core.Models.Querying;
using Aster.Persistence.PostgresJsonb.Querying;
using Aster.Tests.Persistence;
using Npgsql;

namespace Aster.Tests.PostgresJsonb;

/// <summary>
/// SQLite normalizes stored date/times by calling <see cref="QueryDateTimeValue.TryNormalizeDateKey"/>; Postgres
/// runs <see cref="PostgresDateKey"/>'s SQL instead. These pin the two to the same keys, edge cases included.
/// </summary>
public sealed class PostgresJsonbDateKeyTests
{
    public static TheoryData<string> Values() => new()
    {
        // Accepted shapes: offsets as Z, ±hh:mm, ±h:mm or ±hhmm, or none (UTC); up to seven fraction digits.
        "2026-02-01T10:00:00Z",
        "2026-02-01T10:00:00",
        "2026-02-01T10:00:00+01:00",
        "2026-02-01T10:00:00-0130",
        "2026-02-01T10:00:00+1:00",
        "2026-02-01T10:00:00+9:30",
        "2026-02-01T10:00:00-00:00",
        "2026-02-01T10:00:00+14:00",
        "2026-02-01T10:00:00-14:00",
        "2026-02-01T10:00:00.1Z",
        "2026-02-01T10:00:00.12",
        "2026-02-01T10:00:00.1234567Z",
        "2026-02-01T10:00:00.Z",
        "2026-02-01T10:00:00.+01:00",
        "2026-02-01T10:00:00.5+01:00",
        "2024-02-29T10:00:00Z",
        "0001-01-01T00:00:00Z",
        "9999-12-31T23:59:59.9999999Z",
        "2026-12-31T23:30:00-01:00",

        // Rejected: impossible dates and times, offsets out of range, the instant leaving years 1-9999, and
        // anything that isn't exactly the accepted shape.
        "2026-02-30T10:00:00Z",
        "2023-02-29T10:00:00Z",
        "2026-13-01T10:00:00Z",
        "2026-00-10T10:00:00Z",
        "0000-01-01T00:00:00Z",
        "0001-01-01T00:00:00+01:00",
        "9999-12-31T23:59:59-01:00",
        "2026-02-01T24:00:00Z",
        "2026-02-01T23:60:00Z",
        "2026-02-01T23:59:60Z",
        "2026-02-01T10:00:00+14:01",
        "2026-02-01T10:00:00+15:00",
        "2026-02-01T10:00:00+09:60",
        "2026-02-01T10:00:00+01",
        "2026-02-01T10:00:00+100",
        "2026-02-01T10:00:00+1430",
        "2026-02-01T10:00:00+001:00",
        "2026-02-01T10:00:00.12345678Z",
        "2026-02-01T10:00:00,5Z",
        "2026-02-01T10:00Z",
        "2026-02-01T1:00:00Z",
        "2026-1-01T10:00:00Z",
        "2026-02-01t10:00:00Z",
        "2026-02-01T10:00:00z",
        "2026-02-01 10:00:00Z",
        "2026-02-01",
        " 2026-02-01T10:00:00Z",
        "2026-02-01T10:00:00Z ",
        "2026-02-01T10:00:00 +01:00",
        "2026-02-01T10:00:00GMT",
        "2026-02-01T10:00:00+01:30Z",
        "٢٠٢٦-02-01T10:00:00Z",
        "not-a-date",
        "",
    };

    [SkippableTheory]
    [MemberData(nameof(Values))]
    public async Task DateKeySql_MatchesQueryDateTimeValue(string value)
    {
        var expected = QueryDateTimeValue.TryNormalizeDateKey(value, out var key) ? key : null;

        await using var connection = new NpgsqlConnection(PostgresTestDatabase.ServerConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT {PostgresDateKey.Sql("@value")};", connection);
        command.Parameters.AddWithValue("value", value);
        var actual = await command.ExecuteScalarAsync();

        Assert.Equal(expected, actual is DBNull ? null : (string?)actual);
    }
}
