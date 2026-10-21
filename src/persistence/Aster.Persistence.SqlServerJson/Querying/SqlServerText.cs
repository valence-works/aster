using System.Text.Json;

namespace Aster.Persistence.SqlServerJson.Querying;

/// <summary>
/// Case-insensitive text predicates matching the other providers' <c>StringComparison.OrdinalIgnoreCase</c>,
/// independent of the database's default collation. Both sides are upper-cased under one explicit collation
/// (<see cref="CaseCollation"/>), then compared exactly (<see cref="BinaryCollation"/>). Comparing under a
/// case-insensitive collation instead would also equate strings that ordinal comparison keeps apart (<c>ß</c> and
/// <c>ss</c>, or text differing only in ignorable characters); and because <c>=</c> ignores trailing spaces, equality
/// also compares lengths.
/// </summary>
internal static class SqlServerText
{
    /// <summary>The collation whose case mapping <c>UPPER</c> uses.</summary>
    public const string CaseCollation = "Latin1_General_100_CI_AS_SC";

    /// <summary>The code-point collation every comparison and identifier column uses.</summary>
    public const string BinaryCollation = "Latin1_General_100_BIN2";

    /// <summary>
    /// The characters of a text projection's key: 800 bytes, so an index on it (and on the tenant and the clustered
    /// key it carries) stays under the 1,700-byte nonclustered key limit. Longer values keep a prefix as their key.
    /// </summary>
    public const int KeyLength = 400;

    // The options RelationalJsonResourceStore serializes payloads with.
    private static readonly JsonSerializerOptions StoreJsonOptions = new(JsonSerializerDefaults.Web);

    public static string Upper(string textSql) => $"UPPER(({textSql}) COLLATE {CaseCollation}) COLLATE {BinaryCollation}";

    /// <summary>
    /// The upper-cased prefix that text projection keys hold: the column's expression over the stored value, and the
    /// value a query compares with it.
    /// </summary>
    public static string Key(string textSql) =>
        $"CAST(UPPER(LEFT(({textSql}) COLLATE {BinaryCollation}, {KeyLength}) COLLATE {CaseCollation}) AS nvarchar({KeyLength})) COLLATE {BinaryCollation}";

    /// <summary>
    /// A search condition that is true when <paramref name="condition"/> is true and false otherwise (never unknown),
    /// so <c>NOT</c> keeps its meaning, as the other providers' predicates do.
    /// </summary>
    public static string Bool(string condition) => $"(CASE WHEN {condition} THEN 1 ELSE 0 END) = 1";

    public static string EqualsIgnoreCase(string actualSql, string expectedParameter) =>
        Bool(EqualsIgnoreCaseCondition(actualSql, expectedParameter));

    /// <summary>The condition behind <see cref="EqualsIgnoreCase"/>; unknown when <paramref name="actualSql"/> is NULL.</summary>
    public static string EqualsIgnoreCaseCondition(string actualSql, string expectedParameter) =>
        $"DATALENGTH({actualSql}) = DATALENGTH({expectedParameter}) AND {Upper(actualSql)} = {Upper(expectedParameter)}";

    public static string ContainsIgnoreCase(string actualSql, string expectedParameter) =>
        Bool($"({actualSql}) IS NOT NULL AND (DATALENGTH({expectedParameter}) = 0 OR CHARINDEX({Upper(expectedParameter)}, {Upper(actualSql)}) > 0)");

    // LEFT counts UTF-16 code units under the binary collation, as DATALENGTH / 2 does.
    public static string StartsWithIgnoreCase(string actualSql, string expectedParameter) =>
        Bool($"DATALENGTH({actualSql}) >= DATALENGTH({expectedParameter}) AND LEFT({Upper(actualSql)}, DATALENGTH({expectedParameter}) / 2) = {Upper(expectedParameter)}");

    // Code-point order of the upper-cased text: the ordinal ignore-case order of the other providers.
    public static string OrderIgnoreCase(string textSql) => Upper(textSql);

    /// <summary>
    /// A JSON path step for <paramref name="key"/>: the key quoted and escaped exactly as the stores' serializer writes
    /// it. SQL Server compares a path's keys with the document's text before unescaping, so a path only finds a key
    /// the serializer escaped (quotes, apostrophes, non-ASCII letters) when it is escaped the same way.
    /// </summary>
    public static string JsonPathKey(string key) => JsonSerializer.Serialize(key, StoreJsonOptions);
}
