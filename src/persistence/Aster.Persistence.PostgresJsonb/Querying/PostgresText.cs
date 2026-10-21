namespace Aster.Persistence.PostgresJsonb.Querying;

/// <summary>
/// Case-insensitive text predicates matching the other providers' <c>StringComparison.OrdinalIgnoreCase</c>:
/// both sides are upper-cased one character at a time under the database's default collation (identifier
/// columns are <c>COLLATE "C"</c>, whose <c>upper</c> only maps ASCII). A database whose <c>LC_CTYPE</c> is
/// plain <c>C</c> therefore folds ASCII letters only.
/// </summary>
internal static class PostgresText
{
    public static string Upper(string textSql) => $"upper(({textSql}) COLLATE \"default\")";

    // Every predicate is true or false, never NULL, as the SQLite functions are, so NOT keeps its meaning.
    public static string EqualsIgnoreCase(string actualSql, string expectedParameter) =>
        $"COALESCE({Upper(actualSql)} = {Upper(expectedParameter)}, false)";

    public static string ContainsIgnoreCase(string actualSql, string expectedParameter) =>
        $"COALESCE(strpos({Upper(actualSql)}, {Upper(expectedParameter)}) > 0, false)";

    public static string StartsWithIgnoreCase(string actualSql, string expectedParameter) =>
        $"COALESCE(starts_with({Upper(actualSql)}, {Upper(expectedParameter)}), false)";

    // Byte order of the upper-cased text: the ordinal ignore-case order of the other providers.
    public static string OrderIgnoreCase(string textSql) => $"{Upper(textSql)} COLLATE \"C\"";
}
