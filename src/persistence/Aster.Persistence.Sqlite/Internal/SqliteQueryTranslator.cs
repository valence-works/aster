using System.Text;
using Aster.Core.Models.Querying;
using Microsoft.Data.Sqlite;

namespace Aster.Persistence.Sqlite.Internal;

/// <summary>
/// Translates <see cref="ResourceQuery"/> AST to parameterised Sqlite SQL.
/// </summary>
internal sealed class SqliteQueryTranslator
{
    private int parameterIndex;

    /// <summary>
    /// Translates a <see cref="ResourceQuery"/> into a parameterised SQL statement.
    /// </summary>
    public (string Sql, List<SqliteParameter> Parameters) Translate(ResourceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        parameterIndex = 0;

        var parameters = new List<SqliteParameter>();
        var sb = new StringBuilder();

        sb.Append("""
            SELECT r.ResourceId, r.Version, r.VersionId, r.DefinitionId, r.DefinitionVersion,
                   r.AspectsJson, r.CreatedUtc, r.Owner, r.Hash
            FROM ResourceRecord r
            INNER JOIN (
                SELECT ResourceId, MAX(Version) AS MaxVersion
                FROM ResourceRecord
                GROUP BY ResourceId
            ) latest ON r.ResourceId = latest.ResourceId AND r.Version = latest.MaxVersion
            """);

        var conditions = new List<string>();

        // DefinitionId shortcut filter
        if (!string.IsNullOrWhiteSpace(query.DefinitionId))
        {
            var pName = NextParam();
            conditions.Add($"r.DefinitionId = {pName}");
            parameters.Add(new SqliteParameter(pName, query.DefinitionId));
        }

        // Filter expression tree
        if (query.Filter is not null)
        {
            var (filterSql, filterParams) = TranslateFilter(query.Filter);
            conditions.Add(filterSql);
            parameters.AddRange(filterParams);
        }

        if (conditions.Count > 0)
        {
            sb.Append(" WHERE ");
            sb.Append(string.Join(" AND ", conditions));
        }

        // Deterministic sort with tie-break on (ResourceId, Version)
        sb.Append(" ORDER BY r.ResourceId ASC, r.Version ASC");

        // Pagination
        if (query.Take.HasValue)
        {
            var pTake = NextParam();
            sb.Append($" LIMIT {pTake}");
            parameters.Add(new SqliteParameter(pTake, query.Take.Value));
        }

        if (query.Skip.HasValue)
        {
            if (!query.Take.HasValue)
                sb.Append(" LIMIT -1"); // Sqlite requires LIMIT before OFFSET

            var pSkip = NextParam();
            sb.Append($" OFFSET {pSkip}");
            parameters.Add(new SqliteParameter(pSkip, query.Skip.Value));
        }

        return (sb.ToString(), parameters);
    }

    private (string Sql, List<SqliteParameter> Parameters) TranslateFilter(FilterExpression expr)
    {
        return expr switch
        {
            MetadataFilter meta => TranslateMetadata(meta),
            AspectPresenceFilter asp => TranslateAspectPresence(asp),
            FacetValueFilter fv => TranslateFacetValue(fv),
            LogicalExpression logic => TranslateLogical(logic),
            _ => throw new NotSupportedException($"Unknown filter expression type: {expr.GetType().Name}")
        };
    }

    private (string Sql, List<SqliteParameter> Parameters) TranslateMetadata(MetadataFilter filter)
    {
        var column = filter.Field.ToLowerInvariant() switch
        {
            "resourceid" => "r.ResourceId",
            "definitionid" => "r.DefinitionId",
            "owner" => "r.Owner",
            "version" => "r.Version",
            _ => throw new NotSupportedException($"Metadata field '{filter.Field}' is not supported.")
        };

        var pName = NextParam();
        var parameters = new List<SqliteParameter>();

        var sql = filter.Operator switch
        {
            ComparisonOperator.Equals => $"{column} = {pName}",
            ComparisonOperator.Contains => $"{column} LIKE {pName}",
            ComparisonOperator.Range => $"{column} = {pName}", // Range uses the value as-is for simple cases
            _ => throw new NotSupportedException($"Unknown comparator: {filter.Operator}")
        };

        if (filter.Operator == ComparisonOperator.Contains)
            parameters.Add(new SqliteParameter(pName, $"%{filter.Value}%"));
        else
            parameters.Add(new SqliteParameter(pName, filter.Value));

        return (sql, parameters);
    }

    private (string Sql, List<SqliteParameter> Parameters) TranslateAspectPresence(AspectPresenceFilter filter)
    {
        var pName = NextParam();
        var sql = $"json_extract(r.AspectsJson, '$.' || {pName}) IS NOT NULL";
        return (sql, [new SqliteParameter(pName, filter.AspectKey)]);
    }

    private (string Sql, List<SqliteParameter> Parameters) TranslateFacetValue(FacetValueFilter filter)
    {
        var pAspect = NextParam();
        var pFacet = NextParam();
        var pValue = NextParam();
        var parameters = new List<SqliteParameter>
        {
            new(pAspect, filter.AspectKey),
            new(pFacet, filter.FacetDefinitionId),
            new(pValue, filter.Value?.ToString() ?? string.Empty)
        };

        // Use json_extract to reach into the aspect's facet value
        var jsonPath = $"json_extract(json_extract(r.AspectsJson, '$.' || {pAspect}), '$.' || {pFacet})";

        var sql = filter.Operator switch
        {
            ComparisonOperator.Equals => $"{jsonPath} = {pValue}",
            ComparisonOperator.Contains => $"{jsonPath} LIKE '%' || {pValue} || '%'",
            ComparisonOperator.Range => $"{jsonPath} = {pValue}",
            _ => throw new NotSupportedException($"Unknown comparator: {filter.Operator}")
        };

        return (sql, parameters);
    }

    private (string Sql, List<SqliteParameter> Parameters) TranslateLogical(LogicalExpression expr)
    {
        var parameters = new List<SqliteParameter>();
        var parts = new List<string>();

        foreach (var operand in expr.Operands)
        {
            var (sql, ps) = TranslateFilter(operand);
            parts.Add($"({sql})");
            parameters.AddRange(ps);
        }

        var combined = expr.Operator switch
        {
            LogicalOperator.And => string.Join(" AND ", parts),
            LogicalOperator.Or => string.Join(" OR ", parts),
            LogicalOperator.Not when parts.Count == 1 => $"NOT ({parts[0]})",
            LogicalOperator.Not => throw new NotSupportedException("NOT operator expects exactly one operand."),
            _ => throw new NotSupportedException($"Unsupported logical operator: {expr.Operator}")
        };

        return ($"({combined})", parameters);
    }

    private string NextParam() => $"@p{parameterIndex++}";
}
