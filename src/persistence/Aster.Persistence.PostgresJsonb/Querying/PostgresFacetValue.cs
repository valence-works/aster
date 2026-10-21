namespace Aster.Persistence.PostgresJsonb.Querying;

/// <summary>
/// One facet of the row's payload as <c>jsonb</c>: <c>aspects -&gt; aspectKey -&gt; facetId</c>, falling back to the
/// camel-case facet key the way the other providers do. SQL NULL means the aspect or facet is missing; a JSON null
/// is the <c>jsonb</c> value <c>null</c>.
/// </summary>
internal sealed class PostgresFacetValue
{
    private PostgresFacetValue(string json) => Json = json;

    public string Json { get; }

    public string Type => $"jsonb_typeof({Json})";

    /// <summary>The number, for use only where <see cref="Type"/> is <c>number</c>.</summary>
    public string Number => $"({Json} #>> '{{}}')::double precision";

    /// <summary>A present, non-null value; false (not NULL) for a missing facet.</summary>
    public string IsPresent => $"({Json} IS NOT NULL AND {Type} <> 'null')";

    /// <summary>
    /// The value as text, as SQLite's <c>CAST(value AS TEXT)</c> renders it: strings unquoted, numbers as written,
    /// booleans as 1 and 0, JSON null as NULL, and arrays and objects as JSON.
    /// </summary>
    public string Text => $$"""
        CASE {{Type}}
            WHEN 'string' THEN {{Json}} #>> '{}'
            WHEN 'number' THEN {{Json}} #>> '{}'
            WHEN 'boolean' THEN CASE WHEN {{Json}} = 'true'::jsonb THEN '1' ELSE '0' END
            WHEN 'null' THEN NULL
            ELSE {{Json}}::text
        END
        """;

    /// <summary>
    /// A numeric test: <paramref name="predicate"/> over <see cref="Number"/> for numbers, false for other values,
    /// and NULL for a missing facet, as SQLite's <c>type IN ('integer', 'real') AND ...</c> evaluates.
    /// </summary>
    public string WhenNumber(string predicate) =>
        $"(CASE WHEN {Json} IS NULL THEN NULL WHEN {Type} = 'number' THEN {predicate} ELSE false END)";

    public static PostgresFacetValue Create(PostgresParameterBag parameters, string aspectKey, string facetDefinitionId)
    {
        var aspect = $"rv.payload -> 'aspects' -> {parameters.Add(aspectKey)}";
        var keys = FacetCandidates(facetDefinitionId).Select(key => $"{aspect} -> {parameters.Add(key)}").ToList();

        // Parenthesized either way: :: binds tighter than ->, so a bare path would cast only its last key.
        return new(keys.Count == 1 ? $"({keys[0]})" : $"COALESCE({string.Join(", ", keys)})");
    }

    private static IReadOnlyList<string> FacetCandidates(string facetDefinitionId)
    {
        var camel = string.IsNullOrEmpty(facetDefinitionId) || char.IsLower(facetDefinitionId[0])
            ? facetDefinitionId
            : char.ToLowerInvariant(facetDefinitionId[0]) + facetDefinitionId[1..];
        return string.Equals(facetDefinitionId, camel, StringComparison.Ordinal)
            ? [facetDefinitionId]
            : [facetDefinitionId, camel];
    }
}
