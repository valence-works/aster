namespace Aster.Persistence.PostgresJsonb.Querying;

/// <summary>
/// SQL for <c>QueryDateTimeValue.TryNormalizeDateKey</c> over text: the same accepted shapes, and the same UTC key
/// (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>), or NULL. SQLite calls the .NET method as a function; Postgres can't, so
/// this mirrors it, and a test pins the two against each other.
/// </summary>
internal static class PostgresDateKey
{
    // yyyy-MM-ddTHH:mm:ss, an optional fraction of up to seven digits (a bare "." is accepted), then an optional
    // zone: Z, ±h:mm, ±hh:mm or ±hhmm (±hmm is rejected below). Matched without capture groups, which cost far more
    // than the match: a matching string has every field at a fixed position, read with substr instead.
    private const string Pattern =
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{0,7})?(Z|[+-][0-9]{1,2}:?[0-9]{2})?$";

    // After the seconds: the fraction digits and the zone.
    private const string Rest = "substr(v, 20)";
    private const string Fraction =
        $"CASE WHEN left({Rest}, 1) = '.' THEN substr({Rest}, 2, length({Rest}) - 1 - length(ltrim(substr({Rest}, 2), '0123456789'))) ELSE '' END";
    private const string Zone =
        $"CASE WHEN left({Rest}, 1) = '.' THEN ltrim(substr({Rest}, 2), '0123456789') ELSE {Rest} END";

    // The parsed fields are computed once per value: OFFSET 0 keeps Postgres from inlining the subqueries, which
    // would evaluate every field expression once per reference.
    public static string Sql(string textSql) => $"""
        (SELECT CASE
            WHEN v IS NULL OR NOT (v ~ '{Pattern}') THEN NULL
            ELSE (
                SELECT CASE
                    WHEN y < 1 OR mo NOT BETWEEN 1 AND 12 THEN NULL
                    WHEN d NOT BETWEEN 1 AND extract(day FROM make_date(y, mo, 1) + interval '1 month - 1 day')::integer THEN NULL
                    WHEN h > 23 OR mi > 59 OR s > 59 THEN NULL
                    WHEN zone NOT IN ('', 'Z') AND strpos(zone, ':') = 0 AND length(zone) <> 5 THEN NULL
                    WHEN om > 59 OR oh * 60 + om > 840 THEN NULL
                    ELSE (
                        SELECT CASE
                            WHEN utc BETWEEN timestamp '0001-01-01 00:00:00' AND timestamp '9999-12-31 23:59:59'
                            THEN to_char(utc, 'YYYY-MM-DD"T"HH24:MI:SS') || '.' || rpad(fraction, 7, '0') || 'Z'
                        END
                        FROM (SELECT make_timestamp(y, mo, d, h, mi, s) - sign * make_interval(mins => oh * 60 + om) AS utc OFFSET 0) utc_value)
                END
                FROM (
                    SELECT y, mo, d, h, mi, s, fraction, zone,
                        CASE WHEN left(zone, 1) = '-' THEN -1 ELSE 1 END AS sign,
                        CASE WHEN zone IN ('', 'Z') THEN 0
                             WHEN strpos(zone, ':') > 0 THEN split_part(substr(zone, 2), ':', 1)::integer
                             ELSE left(substr(zone, 2), 2)::integer END AS oh,
                        CASE WHEN zone IN ('', 'Z') THEN 0
                             WHEN strpos(zone, ':') > 0 THEN split_part(substr(zone, 2), ':', 2)::integer
                             ELSE right(zone, 2)::integer END AS om
                    FROM (
                        SELECT substr(v, 1, 4)::integer AS y, substr(v, 6, 2)::integer AS mo, substr(v, 9, 2)::integer AS d,
                            substr(v, 12, 2)::integer AS h, substr(v, 15, 2)::integer AS mi, substr(v, 18, 2)::integer AS s,
                            {Fraction} AS fraction, {Zone} AS zone
                        OFFSET 0) fields
                    OFFSET 0) parsed)
        END
        FROM (SELECT ({textSql}) AS v OFFSET 0) input)
        """;
}
