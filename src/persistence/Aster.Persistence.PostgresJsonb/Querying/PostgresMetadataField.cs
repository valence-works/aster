using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.PostgresJsonb.Querying;

internal static class PostgresMetadataField
{
    public static bool IsNumeric(string field) =>
        string.Equals(field, "Version", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The field as text, as the SQLite provider stores it: <c>Created</c> in the round-trip ("O") format.
    /// </summary>
    public static string Text(string field, string path) => field.ToLowerInvariant() switch
    {
        "created" => "to_char(rv.created AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'",
        _ => Column(field, "Metadata field", "unsupported-metadata-field", path),
    };

    /// <summary>
    /// The sort key: identifiers in byte order (as SQLite's binary collation compares them), <c>Created</c> and
    /// <c>Version</c> by value.
    /// </summary>
    public static string SortKey(string field, string path) => field.ToLowerInvariant() switch
    {
        "created" => "rv.created",
        "version" => "rv.version",
        _ => $"{Column(field, "Metadata sort field", "unsupported-metadata-sort-field", path)} COLLATE \"C\"",
    };

    private static string Column(string field, string description, string code, string path) => field.ToLowerInvariant() switch
    {
        "resourceid" => "rv.resource_id",
        "id" => "rv.id",
        "definitionid" => "rv.definition_id",
        "owner" => "rv.owner",
        _ => throw Unsupported(
            code,
            "metadata field",
            $"{description} '{field}' is not supported by the PostgreSQL JSONB query provider.",
            path)
    };
}
