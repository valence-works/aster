using System.Text;
using Aster.Core.Exceptions;
using Aster.Core.Models.Querying;

namespace Aster.Persistence.SqliteJson.Querying;

internal sealed class SqliteQueryBuilder(ResourceQuery query)
{
    private readonly List<string> predicates = [];
    private readonly List<string> orderings = [];
    private readonly List<string> projections = [];

    public SqliteParameterBag Parameters { get; } = new();

    public void AddPredicate(string predicate)
    {
        if (!string.IsNullOrWhiteSpace(predicate))
            predicates.Add(predicate);
    }

    public void AddSorts(IReadOnlyList<SortExpression> sorts)
    {
        for (var index = 0; index < sorts.Count; index++)
        {
            var sort = sorts[index];
            orderings.Add(ResolveOrdering(sort, index));
        }
    }

    public string Build()
    {
        var (baseSql, scopePredicates) = BuildScopeSql();
        var sql = new StringBuilder();
        sql.Append("SELECT ");
        sql.AppendJoin(", ", ["rv.payload", .. projections]);
        sql.AppendLine();
        sql.Append(baseSql);
        predicates.AddRange(scopePredicates);

        if (!string.IsNullOrWhiteSpace(query.DefinitionId))
        {
            var parameter = Parameters.Add(query.DefinitionId);
            predicates.Add($"rv.definition_id = {parameter}");
        }

        if (predicates.Count > 0)
        {
            sql.AppendLine();
            sql.Append("WHERE ");
            sql.AppendJoin(" AND ", predicates.Select(predicate => $"({predicate})"));
        }

        sql.AppendLine();
        sql.Append("ORDER BY ");
        sql.Append(string.Join(", ", Orderings()));

        if (query.Take.HasValue)
        {
            sql.AppendLine();
            sql.Append("LIMIT ");
            sql.Append(Parameters.Add(query.Take.Value));
        }
        else if (query.Skip.HasValue)
        {
            sql.AppendLine();
            sql.Append("LIMIT -1");
        }

        if (query.Skip.HasValue)
        {
            sql.AppendLine();
            sql.Append("OFFSET ");
            sql.Append(Parameters.Add(query.Skip.Value));
        }

        sql.Append(';');
        return sql.ToString();
    }

    private IEnumerable<string> Orderings() =>
        orderings.Count == 0
            ? ["rv.resource_id ASC", "rv.version ASC"]
            : [.. orderings, "rv.resource_id ASC", "rv.version ASC"];

    private (string Sql, IReadOnlyList<string> ScopePredicates) BuildScopeSql() => query.Scope switch
    {
        ResourceVersionScope.Latest => ("""
            FROM resource_versions rv
            INNER JOIN (
                SELECT resource_id, MAX(version) AS version
                FROM resource_versions
                GROUP BY resource_id
            ) latest
                ON latest.resource_id = rv.resource_id
                AND latest.version = rv.version
            """, []),
        ResourceVersionScope.AllVersions => ("""
            FROM resource_versions rv
            """, []),
        ResourceVersionScope.Active => ActiveSql(),
        ResourceVersionScope.Draft => ("""
            FROM resource_versions rv
            """, ["""
                NOT EXISTS (
                    SELECT 1
                    FROM activation_states active_state
                    JOIN json_each(json_extract(active_state.payload, '$.activeVersions')) active_version
                    WHERE active_state.resource_id = rv.resource_id
                      AND CAST(active_version.value AS INTEGER) = rv.version
                )
                """]),
        _ => throw Unsupported(
            "unsupported-scope",
            "scope",
            $"Scope '{query.Scope}' is not supported by the SQLite JSON query provider.",
            "Scope")
    };

    private (string Sql, IReadOnlyList<string> ScopePredicates) ActiveSql()
    {
        var channel = Parameters.Add(query.ActivationChannel);
        return ($$"""
            FROM resource_versions rv
            INNER JOIN activation_states active_state
                ON active_state.resource_id = rv.resource_id
                AND active_state.channel = {{channel}}
            """, ["""
                EXISTS (
                    SELECT 1
                    FROM json_each(json_extract(active_state.payload, '$.activeVersions')) active_version
                    WHERE CAST(active_version.value AS INTEGER) = rv.version
                )
                """]);
    }

    private string ResolveOrdering(SortExpression sort, int index)
    {
        var direction = ResolveDirection(sort.Direction, index);

        if (string.IsNullOrWhiteSpace(sort.AspectKey))
            return $"{ResolveMetadataColumn(sort.Field, index)} {direction}";

        var facet = SqliteFacetValueExpression.Create(Parameters, sort.AspectKey, sort.Field);
        var valueAlias = $"facet_sort_{index}_value";
        var typeAlias = $"facet_sort_{index}_type";
        var isNumeric = $"{typeAlias} IN ('integer', 'real')";

        projections.Add($"{facet.Value} AS {valueAlias}");
        projections.Add($"{facet.Type} AS {typeAlias}");

        return string.Join(", ", [
            $"{valueAlias} IS NULL ASC",
            $"CASE WHEN {isNumeric} THEN CAST({valueAlias} AS REAL) END {direction}",
            $"CASE WHEN NOT ({isNumeric}) THEN CAST({valueAlias} AS TEXT) END COLLATE {SqliteTextBehavior.OrdinalIgnoreCaseCollation} {direction}",
        ]);
    }

    private static string ResolveMetadataColumn(string field, int index) =>
        SqliteMetadataField.ResolveColumn(
            field,
            "Metadata sort field",
            "unsupported-metadata-sort-field",
            "metadata field",
            $"Sorts[{index}].Field");

    private static string ResolveDirection(SortDirection direction, int index) => direction switch
    {
        SortDirection.Ascending => "ASC",
        SortDirection.Descending => "DESC",
        _ => throw Unsupported(
            "unsupported-sort-direction",
            "sort direction",
            $"Sort direction '{direction}' is not supported by the SQLite JSON query provider.",
            $"Sorts[{index}].Direction")
    };

    private static UnsupportedQueryFeatureException Unsupported(
        string code,
        string feature,
        string message,
        string? path = null) =>
        new(code, feature, message, path);
}
