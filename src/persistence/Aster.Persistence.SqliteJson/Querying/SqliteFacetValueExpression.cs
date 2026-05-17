namespace Aster.Persistence.SqliteJson.Querying;

internal sealed record SqliteFacetValueExpression(string Value, string IsNumeric)
{
    public static SqliteFacetValueExpression Create(
        SqliteParameterBag parameters,
        string aspectKey,
        string facetDefinitionId)
    {
        var aspectsPath = parameters.Add(SqliteJsonPath.Aspects);
        var typeAspectsPath = parameters.Add(SqliteJsonPath.Aspects);
        var aspectKeyParameter = parameters.Add(aspectKey);
        var typeAspectKeyParameter = parameters.Add(aspectKey);
        var facetKeys = SqliteJsonPath.FacetCandidates(facetDefinitionId).ToList();
        var facetKeyParameters = facetKeys.Select(parameters.Add).ToList();
        var typeFacetKeyParameters = facetKeys.Select(parameters.Add).ToList();
        var facetKeyList = string.Join(", ", facetKeyParameters);
        var typeFacetKeyList = string.Join(", ", typeFacetKeyParameters);

        return new($"""
            (
                SELECT facet.value
                FROM json_each(json_extract(rv.payload, {aspectsPath})) aspect
                JOIN json_each(aspect.value) facet
                WHERE aspect.key = {aspectKeyParameter}
                  AND facet.key IN ({facetKeyList})
                ORDER BY CASE facet.key WHEN {facetKeyParameters[0]} THEN 0 ELSE 1 END
                LIMIT 1
            )
            """,
            $"""
            (
                SELECT facet.type
                FROM json_each(json_extract(rv.payload, {typeAspectsPath})) aspect
                JOIN json_each(aspect.value) facet
                WHERE aspect.key = {typeAspectKeyParameter}
                  AND facet.key IN ({typeFacetKeyList})
                ORDER BY CASE facet.key WHEN {typeFacetKeyParameters[0]} THEN 0 ELSE 1 END
                LIMIT 1
            ) IN ('integer', 'real')
            """);
    }
}
