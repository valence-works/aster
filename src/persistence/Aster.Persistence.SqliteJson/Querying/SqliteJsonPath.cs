namespace Aster.Persistence.SqliteJson.Querying;

internal static class SqliteJsonPath
{
    public const string Aspects = "$.aspects";

    public static IReadOnlyList<string> FacetCandidates(string facetDefinitionId)
    {
        var camel = ToCamelCase(facetDefinitionId);
        return string.Equals(facetDefinitionId, camel, StringComparison.Ordinal)
            ? [facetDefinitionId]
            : [facetDefinitionId, camel];
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
}
