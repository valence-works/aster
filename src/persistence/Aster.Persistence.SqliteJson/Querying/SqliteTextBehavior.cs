namespace Aster.Persistence.SqliteJson.Querying;

internal static class SqliteTextBehavior
{
    public const string EqualsFunction = "aster_text_equals";
    public const string ContainsFunction = "aster_text_contains";
    public const string OrdinalIgnoreCaseCollation = "aster_ordinal_ignore_case";

    public static bool EqualsIgnoreCase(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    public static bool ContainsIgnoreCase(string? actual, string? expected) =>
        actual is not null
        && expected is not null
        && actual.Contains(expected, StringComparison.OrdinalIgnoreCase);

    public static int CompareIgnoreCase(string? left, string? right) =>
        string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
}
