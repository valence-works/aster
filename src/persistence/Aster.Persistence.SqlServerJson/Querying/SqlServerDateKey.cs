namespace Aster.Persistence.SqlServerJson.Querying;

/// <summary>
/// T-SQL for <c>QueryDateTimeValue.TryNormalizeDateKey</c> over text: the same accepted shapes, and the same UTC key
/// (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>), or NULL. SQLite calls the .NET method as a function; SQL Server can't, so
/// this mirrors it, and a test pins the two against each other. The function is schema-bound and deterministic, so
/// persisted computed columns (date projections) can use it.
/// </summary>
internal static class SqlServerDateKey
{
    /// <summary>
    /// The function's two-part name. Scalar functions must be called schema-qualified, so it lives in <c>dbo</c>.
    /// </summary>
    public const string Function = "dbo.aster_date_key";

    // Input longer than the longest accepted shape (33 characters) is rejected, so a 100-character parameter loses
    // nothing: a longer argument is truncated to 100 characters, still too long to be accepted.
    public const string CreateFunction = $"""
        CREATE OR ALTER FUNCTION {Function} (@value nvarchar(100))
        RETURNS nvarchar(28)
        WITH SCHEMABINDING, RETURNS NULL ON NULL INPUT
        AS
        BEGIN
            -- Every pattern and comparison is under BIN2: [0-9] matches ASCII digits only, and letters match by case.
            -- (A variable keeps the database's collation whatever is assigned to it, so each use says BIN2.)
            DECLARE @v nvarchar(100) = @value;
            DECLARE @length int = DATALENGTH(@v) / 2;
            IF @length < 19 OR @length > 33
                OR LEFT(@v, 19) COLLATE {SqlServerText.BinaryCollation} NOT LIKE N'[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]'
                RETURN NULL;

            -- After the seconds: an optional fraction of up to seven digits (a bare "." is accepted), then an optional
            -- zone: Z, ±h:mm, ±hh:mm or ±hhmm.
            DECLARE @rest nvarchar(100) = SUBSTRING(@v, 20, 100);
            DECLARE @fraction nvarchar(7) = N'';
            IF LEFT(@rest, 1) COLLATE {SqlServerText.BinaryCollation} = N'.'
            BEGIN
                DECLARE @digits int = PATINDEX(N'%[^0-9]%', (SUBSTRING(@rest, 2, 100) + N'x') COLLATE {SqlServerText.BinaryCollation}) - 1;
                IF @digits > 7
                    RETURN NULL;

                SET @fraction = SUBSTRING(@rest, 2, @digits);
                SET @rest = SUBSTRING(@rest, 2 + @digits, 100);
            END

            DECLARE @zoneLength int = DATALENGTH(@rest) / 2;
            DECLARE @sign int = 0, @offsetHours int = 0, @offsetMinutes int = 0;
            IF @zoneLength = 0 OR (@zoneLength = 1 AND @rest COLLATE {SqlServerText.BinaryCollation} = N'Z')
                SET @sign = 0;
            ELSE IF @zoneLength = 5 AND @rest COLLATE {SqlServerText.BinaryCollation} LIKE N'[-+][0-9]:[0-9][0-9]'
                SELECT @offsetHours = CAST(SUBSTRING(@rest, 2, 1) AS int), @offsetMinutes = CAST(SUBSTRING(@rest, 4, 2) AS int);
            ELSE IF @zoneLength = 6 AND @rest COLLATE {SqlServerText.BinaryCollation} LIKE N'[-+][0-9][0-9]:[0-9][0-9]'
                SELECT @offsetHours = CAST(SUBSTRING(@rest, 2, 2) AS int), @offsetMinutes = CAST(SUBSTRING(@rest, 5, 2) AS int);
            ELSE IF @zoneLength = 5 AND @rest COLLATE {SqlServerText.BinaryCollation} LIKE N'[-+][0-9][0-9][0-9][0-9]'
                SELECT @offsetHours = CAST(SUBSTRING(@rest, 2, 2) AS int), @offsetMinutes = CAST(SUBSTRING(@rest, 4, 2) AS int);
            ELSE
                RETURN NULL;

            IF @zoneLength > 1
                SET @sign = CASE WHEN LEFT(@rest, 1) COLLATE {SqlServerText.BinaryCollation} = N'-' THEN -1 ELSE 1 END;

            DECLARE @year int = CAST(SUBSTRING(@v, 1, 4) AS int), @month int = CAST(SUBSTRING(@v, 6, 2) AS int),
                @day int = CAST(SUBSTRING(@v, 9, 2) AS int), @hour int = CAST(SUBSTRING(@v, 12, 2) AS int),
                @minute int = CAST(SUBSTRING(@v, 15, 2) AS int), @second int = CAST(SUBSTRING(@v, 18, 2) AS int);
            IF @year < 1 OR @month NOT BETWEEN 1 AND 12 OR @hour > 23 OR @minute > 59 OR @second > 59
                OR @offsetMinutes > 59 OR @offsetHours * 60 + @offsetMinutes > 840
                RETURN NULL;

            IF @day NOT BETWEEN 1 AND DAY(EOMONTH(DATEFROMPARTS(@year, @month, 1)))
                RETURN NULL;

            -- The instant must stay within years 1-9999 once the offset is removed; checked in minutes first, because
            -- DATEADD fails rather than returning NULL outside that range.
            DECLARE @local datetime2(0) = DATETIME2FROMPARTS(@year, @month, @day, @hour, @minute, @second, 0, 0);
            DECLARE @offset int = @sign * (@offsetHours * 60 + @offsetMinutes);
            DECLARE @utcMinutes bigint = DATEDIFF_BIG(minute, DATETIME2FROMPARTS(1, 1, 1, 0, 0, 0, 0, 0), @local) - @offset;
            IF @utcMinutes < 0 OR @utcMinutes > DATEDIFF_BIG(minute, DATETIME2FROMPARTS(1, 1, 1, 0, 0, 0, 0, 0), DATETIME2FROMPARTS(9999, 12, 31, 23, 59, 0, 0, 0))
                RETURN NULL;

            RETURN CONVERT(nvarchar(19), DATEADD(minute, -@offset, @local), 126) + N'.' + LEFT(@fraction + N'0000000', 7) + N'Z';
        END
        """;

    /// <summary>
    /// The date key of <paramref name="textSql"/>, in the binary collation keys compare under.
    /// </summary>
    public static string Of(string textSql) => $"{Function}({textSql}) COLLATE {SqlServerText.BinaryCollation}";
}
