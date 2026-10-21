namespace Aster.Persistence.SqlServerJson.Querying;

/// <summary>
/// One facet of the row's payload, read by an <c>APPLY</c> over <c>OPENJSON</c>: the value under
/// <c>aspects.aspectKey.facetId</c>, falling back to the camel-case facet key the way the other providers do. The
/// applied row's <c>json_type</c> is <c>OPENJSON</c>'s type (0 null, 1 string, 2 number, 3 boolean, 4 array,
/// 5 object), NULL when the aspect or facet is missing; <c>json_value</c> is the value's text (strings unescaped, other
/// values as JSON). <c>OPENJSON</c> rather than <c>JSON_VALUE</c>: it reads values of any length, and arrays.
/// </summary>
internal sealed class SqlServerFacetValue(string alias)
{
    public string Type => $"{alias}.json_type";

    public string Json => $"{alias}.json_value";

    /// <summary>
    /// The array's JSON, or NULL for any other value: what <c>OPENJSON</c> enumerates. Guarded by a <c>CASE</c> because
    /// SQL Server may evaluate an <c>OPENJSON</c> before the type test beside it, and a string's text isn't JSON.
    /// </summary>
    public string ArrayJson => $"CASE WHEN {Type} = 4 THEN {Json} END";

    /// <summary>The number, for use only where <see cref="Type"/> is 2.</summary>
    public string Number => $"TRY_CONVERT(float, {Json})";

    /// <summary>A present, non-null value. Unknown (not false) for a missing facet, so use it inside <see cref="SqlServerText.Bool"/>.</summary>
    public string IsPresent => $"{Type} <> 0";

    /// <summary>
    /// The value as text, as SQLite's <c>CAST(value AS TEXT)</c> renders it: strings unescaped, numbers as written,
    /// booleans as 1 and 0, JSON null as NULL, and arrays and objects as JSON.
    /// </summary>
    public string Text =>
        $"(CASE WHEN {Type} = 3 THEN CASE WHEN {Json} = N'true' THEN N'1' ELSE N'0' END WHEN {Type} = 0 THEN NULL ELSE {Json} END)";

    /// <summary>
    /// A numeric test: <paramref name="predicate"/> over <see cref="Number"/> for numbers, false for other values,
    /// and unknown for a missing facet, as SQLite's <c>type IN ('integer', 'real') AND ...</c> evaluates.
    /// </summary>
    public string WhenNumber(string predicate) =>
        $"(CASE WHEN {Type} IS NULL THEN NULL WHEN {Type} = 2 AND {predicate} THEN 1 ELSE 0 END) = 1";

    /// <summary>The UTC date key of a date/time string, or NULL (see <see cref="SqlServerDateKey"/>).</summary>
    public string DateKey => SqlServerDateKey.Of(Json);

    /// <summary>
    /// The derived table an <c>APPLY</c> reads the facet from, aliased <paramref name="alias"/>. Each key is rendered
    /// with <paramref name="key"/>: a parameter in queries, a literal in projection triggers.
    /// </summary>
    /// <remarks>
    /// The aspect and the facet are found by enumerating keys, which <c>OPENJSON</c> returns unescaped, rather than by
    /// a JSON path: SQL Server matches a path's keys against the document's escaped text, so a key the serializer
    /// escapes (<c>\u0027</c>, non-ASCII letters) would not be found.
    /// </remarks>
    public static string Source(Func<string, string> key, string payload, string aspectKey, string facetDefinitionId, string alias)
    {
        var candidates = FacetCandidates(facetDefinitionId).Select(key).ToList();
        var match = string.Join(" OR ", candidates.Select(static candidate => $"facet.[key] = {candidate}"));

        // The facet ID wins over its camel-case form when both exist, as COALESCE over the two paths does elsewhere.
        var order = candidates.Count == 1 ? "" : $" ORDER BY CASE WHEN facet.[key] = {candidates[0]} THEN 0 ELSE 1 END";
        return $"(SELECT TOP (1) facet.[type] AS json_type, facet.[value] AS json_value FROM OPENJSON({payload}, N'$.aspects') aspect "
            + "CROSS APPLY OPENJSON(CASE WHEN aspect.[type] = 5 THEN aspect.[value] END) facet "
            + $"WHERE aspect.[key] = {key(aspectKey)} AND ({match}){order}) {alias}";
    }

    public static IReadOnlyList<string> FacetCandidates(string facetDefinitionId)
    {
        var camel = string.IsNullOrEmpty(facetDefinitionId) || char.IsLower(facetDefinitionId[0])
            ? facetDefinitionId
            : char.ToLowerInvariant(facetDefinitionId[0]) + facetDefinitionId[1..];
        return string.Equals(facetDefinitionId, camel, StringComparison.Ordinal)
            ? [facetDefinitionId]
            : [facetDefinitionId, camel];
    }
}

/// <summary>
/// The facets a query reads, each applied once to the row however many predicates and sort keys use it.
/// </summary>
internal sealed class SqlServerFacetSources(SqlServerParameterBag parameters)
{
    private readonly Dictionary<(string AspectKey, string FacetId), SqlServerFacetValue> facets = [];
    private readonly List<string> applies = [];

    public IReadOnlyList<string> Applies => applies;

    public SqlServerFacetValue Get(string aspectKey, string facetDefinitionId)
    {
        if (facets.TryGetValue((aspectKey, facetDefinitionId), out var existing))
            return existing;

        var alias = $"facet_{facets.Count}";
        applies.Add($"OUTER APPLY {SqlServerFacetValue.Source(parameters.Add, "rv.payload", aspectKey, facetDefinitionId, alias)}");
        var value = new SqlServerFacetValue(alias);
        facets[(aspectKey, facetDefinitionId)] = value;
        return value;
    }
}
