using Aster.Core.Exceptions;

namespace Aster.Persistence.SqliteJson.Querying;

internal static class SqliteMetadataField
{
    public static string ResolveColumn(
        string field,
        string description,
        string code = "unsupported-metadata-field",
        string feature = "metadata field",
        string? path = null) => field.ToLowerInvariant() switch
    {
        "resourceid" => "rv.resource_id",
        "id" => "rv.id",
        "definitionid" => "rv.definition_id",
        "owner" => "rv.owner",
        "version" => "rv.version",
        "created" => "rv.created",
        _ => throw Unsupported(
            code,
            feature,
            $"{description} '{field}' is not supported by the SQLite JSON query provider.",
            path)
    };

    public static bool IsNumeric(string field) =>
        string.Equals(field, "Version", StringComparison.OrdinalIgnoreCase);

    private static UnsupportedQueryFeatureException Unsupported(
        string code,
        string feature,
        string message,
        string? path = null) =>
        new(code, feature, message, path);
}
