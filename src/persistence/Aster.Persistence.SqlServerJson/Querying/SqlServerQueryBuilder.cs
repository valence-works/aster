using System.Text;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.SqlServerJson.Querying;

internal sealed class SqlServerQueryBuilder
{
    private readonly ResourceQuery query;
    private readonly List<string> predicates = [];
    private readonly List<(string Key, string Direction)> orderings = [];

    public SqlServerQueryBuilder(ResourceQuery query)
    {
        this.query = query;
        Facets = new SqlServerFacetSources(Parameters);
    }

    public SqlServerParameterBag Parameters { get; } = new();

    public SqlServerFacetSources Facets { get; }

    public void AddPredicate(string predicate)
    {
        if (!string.IsNullOrWhiteSpace(predicate))
            predicates.Add(predicate);
    }

    public void AddSorts(IReadOnlyList<SortExpression> sorts)
    {
        // SQL Server rejects a key listed twice; a repeated key orders nothing the first didn't, so it is skipped.
        for (var index = 0; index < sorts.Count; index++)
        {
            foreach (var ordering in ResolveOrderings(sorts[index], index))
            {
                if (!orderings.Any(existing => existing.Key == ordering.Key))
                    orderings.Add(ordering);
            }
        }
    }

    public string Build()
    {
        var tenantId = Parameters.Add(TenantScopeResolver.Resolve(query.TenantScope).TenantId);
        var (baseSql, scopePredicates) = BuildScopeSql(tenantId);
        var sql = new StringBuilder();
        sql.AppendLine("SELECT rv.payload");
        sql.Append(baseSql);
        sql.Append(BuildLifecycleMarkerJoin());
        foreach (var apply in Facets.Applies)
        {
            sql.AppendLine();
            sql.Append(apply);
        }

        predicates.Add($"rv.tenant_id = {tenantId}");
        predicates.AddRange(scopePredicates);
        AddLifecycleStatePredicate();

        if (!string.IsNullOrWhiteSpace(query.DefinitionId))
            predicates.Add($"rv.definition_id = {Parameters.Add(query.DefinitionId)}");

        // FETCH NEXT needs at least one row, so an empty page selects nothing instead.
        if (query.Take == 0)
            predicates.Add("1 = 0");

        sql.AppendLine();
        sql.Append("WHERE ");
        sql.AppendJoin(" AND ", predicates.Select(static predicate => $"({predicate})"));

        sql.AppendLine();
        sql.Append("ORDER BY ");
        var tiebreakers = new[] { "rv.resource_id", "rv.version" }
            .Where(key => !orderings.Any(ordering => ordering.Key == key))
            .Select(static key => $"{key} ASC");
        sql.AppendJoin(", ", [.. orderings.Select(static ordering => $"{ordering.Key} {ordering.Direction}"), .. tiebreakers]);

        if (query.Skip.HasValue || query.Take.HasValue)
        {
            sql.AppendLine();
            sql.Append($"OFFSET {Parameters.Add(query.Skip ?? 0)} ROWS");
            if (query.Take is > 0)
                sql.Append($" FETCH NEXT {Parameters.Add(query.Take.Value)} ROWS ONLY");
        }

        sql.Append(';');
        return sql.ToString();
    }

    /// <summary>
    /// A condition over the activation row aliased <paramref name="activationAlias"/>: its <c>activeVersions</c> array
    /// contains <paramref name="version"/>.
    /// </summary>
    public static string ActiveVersionsContain(string activationAlias, string version) =>
        $"EXISTS (SELECT 1 FROM OPENJSON({activationAlias}.payload, N'$.activeVersions') active_version WHERE active_version.[type] = 2 AND TRY_CONVERT(int, active_version.[value]) = {version})";

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
        predicates.Add($"COALESCE(TRY_CONVERT(int, JSON_VALUE(lifecycle_marker.payload, N'$.state')), 0) = {parameter}");
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
            """, [ActiveVersionsContain("active_state", "rv.version")]),
        ResourceVersionScope.Draft => ("FROM resource_versions rv", [$"""
            NOT EXISTS (
                SELECT 1
                FROM activation_states active_state
                WHERE active_state.tenant_id = rv.tenant_id
                  AND active_state.resource_id = rv.resource_id
                  AND {ActiveVersionsContain("active_state", "rv.version")}
            )
            """]),
        _ => throw Unsupported(
            "unsupported-scope",
            "scope",
            $"Scope '{query.Scope}' is not supported by the SQL Server JSON query provider.",
            "Scope")
    };

    // SQL Server sorts NULL below every value, as SQLite does.
    private IEnumerable<(string Key, string Direction)> ResolveOrderings(SortExpression sort, int index)
    {
        var direction = sort.Direction switch
        {
            SortDirection.Ascending => "ASC",
            SortDirection.Descending => "DESC",
            _ => throw Unsupported(
                "unsupported-sort-direction",
                "sort direction",
                $"Sort direction '{sort.Direction}' is not supported by the SQL Server JSON query provider.",
                $"Sorts[{index}].Direction")
        };

        if (string.IsNullOrWhiteSpace(sort.AspectKey))
            return [(SqlServerMetadataField.SortKey(sort.Field, $"Sorts[{index}].Field"), direction)];

        // Missing and null values last, then numbers by value, then other values by ordinal ignore-case text.
        var facet = Facets.Get(sort.AspectKey, sort.Field);
        return [
            ($"CASE WHEN {facet.Type} IS NULL OR {facet.Type} = 0 THEN 1 ELSE 0 END", "ASC"),
            ($"CASE WHEN {facet.Type} = 2 THEN {facet.Number} END", direction),
            ($"CASE WHEN {facet.Type} <> 2 THEN {SqlServerText.OrderIgnoreCase(facet.Text)} END", direction),
        ];
    }
}
