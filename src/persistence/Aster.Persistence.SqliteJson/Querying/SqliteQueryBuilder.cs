using System.Text;
using Aster.Core.Exceptions;
using Aster.Core.Models.Querying;

namespace Aster.Persistence.SqliteJson.Querying;

internal sealed class SqliteQueryBuilder(ResourceQuery query)
{
    private readonly List<string> predicates = [];
    private readonly List<string> orderings = [];

    public SqliteParameterBag Parameters { get; } = new();

    public void AddPredicate(string predicate)
    {
        if (!string.IsNullOrWhiteSpace(predicate))
            predicates.Add(predicate);
    }

    public void AddSorts(IReadOnlyList<SortExpression> sorts)
    {
        foreach (var sort in sorts)
            orderings.Add($"{ResolveMetadataColumn(sort)} {ResolveDirection(sort.Direction)}");
    }

    public string Build()
    {
        var sql = new StringBuilder(BaseSql());

        AddScopePredicates();

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
        sql.Append(orderings.Count == 0 ? "rv.resource_id ASC, rv.version ASC" : string.Join(", ", orderings));

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

    private string BaseSql() => query.Scope switch
    {
        ResourceVersionScope.Latest => """
            SELECT rv.payload
            FROM resource_versions rv
            INNER JOIN (
                SELECT resource_id, MAX(version) AS version
                FROM resource_versions
                GROUP BY resource_id
            ) latest
                ON latest.resource_id = rv.resource_id
                AND latest.version = rv.version
            """,
        ResourceVersionScope.AllVersions => """
            SELECT rv.payload
            FROM resource_versions rv
            """,
        ResourceVersionScope.Active => ActiveSql(),
        ResourceVersionScope.Draft => """
            SELECT rv.payload
            FROM resource_versions rv
            """,
        _ => throw Unsupported($"Resource version scope '{query.Scope}'")
    };

    private void AddScopePredicates()
    {
        if (query.Scope == ResourceVersionScope.Active)
        {
            predicates.Add("""
                EXISTS (
                    SELECT 1
                    FROM json_each(json_extract(active_state.payload, '$.activeVersions')) active_version
                    WHERE CAST(active_version.value AS INTEGER) = rv.version
                )
                """);
        }

        if (query.Scope == ResourceVersionScope.Draft)
        {
            predicates.Add("""
                NOT EXISTS (
                    SELECT 1
                    FROM activation_states active_state
                    JOIN json_each(json_extract(active_state.payload, '$.activeVersions')) active_version
                    WHERE active_state.resource_id = rv.resource_id
                      AND CAST(active_version.value AS INTEGER) = rv.version
                )
                """);
        }
    }

    private string ActiveSql()
    {
        var channel = Parameters.Add(query.ActivationChannel);
        return $$"""
            SELECT rv.payload
            FROM resource_versions rv
            INNER JOIN activation_states active_state
                ON active_state.resource_id = rv.resource_id
                AND active_state.channel = {{channel}}
            """;
    }

    private static string ResolveMetadataColumn(SortExpression sort)
    {
        if (!string.IsNullOrWhiteSpace(sort.AspectKey))
            throw Unsupported("Facet sorting");

        return SqliteMetadataField.ResolveColumn(sort.Field, "Metadata sort field");
    }

    private static string ResolveDirection(SortDirection direction) => direction switch
    {
        SortDirection.Ascending => "ASC",
        SortDirection.Descending => "DESC",
        _ => throw Unsupported($"Sort direction '{direction}'")
    };

    private static UnsupportedQueryFeatureException Unsupported(string feature) =>
        new($"{feature} is not supported by the SQLite JSON query provider.");
}
