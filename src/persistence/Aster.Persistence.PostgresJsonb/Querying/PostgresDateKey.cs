namespace Aster.Persistence.PostgresJsonb.Querying;

/// <summary>
/// SQL for <c>QueryDateTimeValue.TryNormalizeDateKey</c> over text: the same accepted shapes, and the same UTC key
/// (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>), or NULL. SQLite calls the .NET method as a function; Postgres can't, so
/// this mirrors it, and a test pins the two against each other.
/// </summary>
internal static class PostgresDateKey
{
    // yyyy-MM-ddTHH:mm:ss, an optional fraction of up to seven digits (a bare "." is accepted), then an optional
    // zone: Z, ±h:mm, ±hh:mm or ±hhmm. Groups: 1-6 date and time, 7 fraction, 9-11 or 12-14 the offset.
    private const string Pattern =
        "^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\\.([0-9]{0,7}))?"
        + "(Z|([+-])([0-9]{1,2}):([0-9]{2})|([+-])([0-9]{2})([0-9]{2}))?$";

    private const string OffsetMinutes =
        "(COALESCE(m[10], m[13], '0')::integer * 60 + COALESCE(m[11], m[14], '0')::integer)";

    // CASE evaluates its branches in order, so each cast and make_* call only sees values the earlier
    // branches have bounded.
    public static string Sql(string textSql) => $"""
        (SELECT CASE
            WHEN m IS NULL THEN NULL
            WHEN m[1]::integer < 1 OR m[2]::integer NOT BETWEEN 1 AND 12 THEN NULL
            WHEN m[3]::integer NOT BETWEEN 1 AND extract(day FROM make_date(m[1]::integer, m[2]::integer, 1) + interval '1 month - 1 day')::integer THEN NULL
            WHEN m[4]::integer > 23 OR m[5]::integer > 59 OR m[6]::integer > 59 THEN NULL
            WHEN COALESCE(m[11], m[14], '0')::integer > 59 OR {OffsetMinutes} > 840 THEN NULL
            ELSE (
                SELECT CASE
                    WHEN utc BETWEEN timestamp '0001-01-01 00:00:00' AND timestamp '9999-12-31 23:59:59'
                    THEN to_char(utc, 'YYYY-MM-DD"T"HH24:MI:SS') || '.' || rpad(COALESCE(m[7], ''), 7, '0') || 'Z'
                END
                FROM (SELECT make_timestamp(m[1]::integer, m[2]::integer, m[3]::integer, m[4]::integer, m[5]::integer, m[6]::integer)
                    - (CASE WHEN COALESCE(m[9], m[12]) = '-' THEN -1 ELSE 1 END) * make_interval(mins => {OffsetMinutes}) AS utc) utc_value)
        END
        FROM (SELECT regexp_match({textSql}, '{Pattern}') AS m) date_parts)
        """;
}
