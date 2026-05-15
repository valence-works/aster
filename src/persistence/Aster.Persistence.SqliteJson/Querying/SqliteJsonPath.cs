namespace Aster.Persistence.SqliteJson.Querying;

internal static class SqliteJsonPath
{
    public static string Aspect(string aspectKey) =>
        "$" + Segment("aspects") + Segment(aspectKey);

    public static IReadOnlyList<string> FacetCandidates(string aspectKey, string facetDefinitionId)
    {
        var camel = ToCamelCase(facetDefinitionId);
        return string.Equals(facetDefinitionId, camel, StringComparison.Ordinal)
            ? [Aspect(aspectKey) + Segment(facetDefinitionId)]
            : [Aspect(aspectKey) + Segment(facetDefinitionId), Aspect(aspectKey) + Segment(camel)];
    }

    private static string Segment(string value) =>
        ".\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
}
