using System.Text;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.PostgresJsonb.Querying;

internal sealed class PostgresQueryBuilder(ResourceQuery query)
{
    private readonly List<string> predicates = [];
    private readonly List<string> orderings = [];

    public PostgresParameterBag Parameters { get; } = new();

    public void AddPredicate(string predicate)
    {
        if (!string.IsNullOrWhiteSpace(predicate))
            predicates.Add(predicate);
    }

    public void AddSorts(IReadOnlyList<SortExpression> sorts)
    {
        for (var index = 0; index < sorts.Count; index++)
            orderings.AddRange(ResolveOrderings(sorts[index], index));
    }

    public string Build()
    {
        var tenantId = Parameters.Add(TenantScopeResolver.Resolve(query.TenantScope).TenantId);
        var (baseSql, scopePredicates) = BuildScopeSql(tenantId);
        var sql = new StringBuilder();
        sql.AppendLine("SELECT rv.payload");
        sql.Append(baseSql);
        sql.Append(BuildLifecycleMarkerJoin());
        predicates.Add($"rv.tenant_id = {tenantId}");
        predicates.AddRange(scopePredicates);
        AddLifecycleStatePredicate();

        if (!string.IsNullOrWhiteSpace(query.DefinitionId))
            predicates.Add($"rv.definition_id = {Parameters.Add(query.DefinitionId)}");

        sql.AppendLine();
        sql.Append("WHERE ");
        sql.AppendJoin(" AND ", predicates.Select(predicate => $"({predicate})"));

        sql.AppendLine();
        sql.Append("ORDER BY ");
        sql.AppendJoin(", ", [.. orderings, "rv.resource_id ASC", "rv.version ASC"]);

        if (query.Take.HasValue)
        {
            sql.AppendLine();
            sql.Append("LIMIT ");
            sql.Append(Parameters.Add(query.Take.Value));
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

    private string BuildLifecycleMarkerJoin() =>
        query.LifecycleState is null
            ? string.Empty
            : """

            LEFT JOIN lifecycle_markers lifecycle_marker
                ON lifecycle_marker.tenant_id = rv.tenant_id
                AND lifecycle_marker.resource_id = rv.resource_id
            """;

    private void AddLifecycleStatePredicate()
    {
        if (query.LifecycleState is not { } lifecycleState)
            return;

        var parameter = Parameters.Add((int)lifecycleState);
        predicates.Add($"COALESCE((lifecycle_marker.payload ->> 'state')::integer, 0) = {parameter}");
    }

    private (string Sql, IReadOnlyList<string> ScopePredicates) BuildScopeSql(string tenantId) => query.Scope switch
    {
        ResourceVersionScope.Latest => ($$"""
            FROM resource_versions rv
            INNER JOIN (
                SELECT tenant_id, resource_id, MAX(version) AS version
                FROM resource_versions
                WHERE tenant_id = {{tenantId}}
                GROUP BY tenant_id, resource_id
            ) latest
                ON latest.tenant_id = rv.tenant_id
                AND latest.resource_id = rv.resource_id
                AND latest.version = rv.version
            """, []),
        ResourceVersionScope.AllVersions => ("FROM resource_versions rv", []),
        ResourceVersionScope.Active => ($$"""
            FROM resource_versions rv
            INNER JOIN activation_states active_state
                ON active_state.tenant_id = rv.tenant_id
                AND active_state.resource_id = rv.resource_id
                AND active_state.channel = {{Parameters.Add(query.ActivationChannel!)}}
            """, ["active_state.payload -> 'activeVersions' @> to_jsonb(rv.version)"]),
        ResourceVersionScope.Draft => ("FROM resource_versions rv", ["""
            NOT EXISTS (
                SELECT 1
                FROM activation_states active_state
                WHERE active_state.tenant_id = rv.tenant_id
                  AND active_state.resource_id = rv.resource_id
                  AND active_state.payload -> 'activeVersions' @> to_jsonb(rv.version)
            )
            """]),
        _ => throw Unsupported(
            "unsupported-scope",
            "scope",
            $"Scope '{query.Scope}' is not supported by the PostgreSQL JSONB query provider.",
            "Scope")
    };

    // SQLite sorts NULL below every value; Postgres sorts it above unless told, so each key says where NULLs go.
    private IEnumerable<string> ResolveOrderings(SortExpression sort, int index)
    {
        var direction = sort.Direction switch
        {
            SortDirection.Ascending => "ASC NULLS FIRST",
            SortDirection.Descending => "DESC NULLS LAST",
            _ => throw Unsupported(
                "unsupported-sort-direction",
                "sort direction",
                $"Sort direction '{sort.Direction}' is not supported by the PostgreSQL JSONB query provider.",
                $"Sorts[{index}].Direction")
        };

        if (string.IsNullOrWhiteSpace(sort.AspectKey))
            return [$"{PostgresMetadataField.SortKey(sort.Field, $"Sorts[{index}].Field")} {direction}"];

        // Missing and null values last, then numbers by value, then other values by ordinal ignore-case text.
        var facet = PostgresFacetValue.Create(Parameters, sort.AspectKey, sort.Field);
        return [
            $"NOT {facet.IsPresent} ASC",
            $"CASE WHEN {facet.Type} = 'number' THEN {facet.Number} END {direction}",
            $"CASE WHEN {facet.Type} <> 'number' THEN {PostgresText.OrderIgnoreCase(facet.Text)} END {direction}",
        ];
    }
}
