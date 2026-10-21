using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.SqlServerJson.Querying;

internal static class SqlServerMetadataField
{
    public static bool IsNumeric(string field) =>
        string.Equals(field, "Version", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The field as text, as the SQLite provider stores it: <c>Created</c> in the round-trip ("O") format, which
    /// always has seven fraction digits (style 126 drops a zero fraction, so the seconds and fraction are built here).
    /// </summary>
    public static string Text(string field, string path) => field.ToLowerInvariant() switch
    {
        "created" => "(CONVERT(nvarchar(19), rv.created, 126) + N'.' + RIGHT(N'000000' + CAST(DATEPART(nanosecond, rv.created) / 100 AS nvarchar(7)), 7) + N'Z')",
        _ => Column(field, "Metadata field", "unsupported-metadata-field", path),
    };

    /// <summary>
    /// The sort key: identifiers in code-point order (their columns are binary-collated, as SQLite compares them),
    /// <c>Created</c> and <c>Version</c> by value.
    /// </summary>
    public static string SortKey(string field, string path) => field.ToLowerInvariant() switch
    {
        "created" => "rv.created",
        "version" => "rv.version",
        _ => Column(field, "Metadata sort field", "unsupported-metadata-sort-field", path),
    };

    /// <summary>The text column behind an identifier field, or null for other fields.</summary>
    public static string? TextColumn(string field) => field.ToLowerInvariant() switch
    {
        "resourceid" => "resource_id",
        "id" => "id",
        "definitionid" => "definition_id",
        "owner" => "owner",
        _ => null,
    };

    private static string Column(string field, string description, string code, string path) =>
        TextColumn(field) is { } column
            ? $"rv.{column}"
            : throw Unsupported(
                code,
                "metadata field",
                $"{description} '{field}' is not supported by the SQL Server JSON query provider.",
                path);
}
