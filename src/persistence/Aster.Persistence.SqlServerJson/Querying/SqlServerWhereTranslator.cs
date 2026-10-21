using System.Globalization;
using Aster.Core.Models.Querying;
using Aster.Persistence.Relational.Querying;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.SqlServerJson.Querying;

/// <summary>
/// Translates a filter to a parameterized search condition over <c>rv.payload</c>. It mirrors the SQLite and Postgres
/// translations predicate for predicate, including where SQL's NULL logic makes a missing facet unknown rather than
/// false, so every provider returns the same rows under NOT as well. T-SQL has no boolean values, so each leaf is a
/// search condition: two-valued ones through <see cref="SqlServerText.Bool"/>, numeric ones through
/// <see cref="SqlServerFacetValue.WhenNumber"/>. Where a declared index projection covers a predicate that isn't
/// negated, an index-matching conjunct is added beside it (<see cref="SqlServerIndexConjuncts"/>).
/// </summary>
internal sealed class SqlServerWhereTranslator(SqlServerParameterBag parameters, SqlServerFacetSources facets, SqlServerIndexConjuncts conjuncts)
{
    public SqlServerWhereTranslator(SqlServerParameterBag parameters, SqlServerFacetSources facets)
        : this(parameters, facets, new SqlServerIndexConjuncts(parameters, []))
    {
    }

    public string Translate(FilterExpression expression) => Translate(expression, positive: true);

    public void Validate(FilterExpression expression) => ValidateRange(expression);

    private string Translate(FilterExpression expression, bool positive) => expression switch
    {
        MetadataFilter metadata => WithIndex(TranslateMetadata(metadata), metadata, positive),
        AspectPresenceFilter aspect => TranslateAspectPresence(aspect),
        FacetValueFilter facet => WithIndex(TranslateFacetValue(facet), facet, positive),
        LogicalExpression logical => TranslateLogical(logical, positive),
        _ => throw Unsupported(
            "unsupported-filter-type",
            "predicate",
            $"Filter expression '{expression.GetType().Name}' is not supported by the SQL Server JSON query provider.",
            "Filter")
    };

    private string TranslateMetadata(MetadataFilter filter)
    {
        if (SqlServerMetadataField.IsNumeric(filter.Field))
        {
            return filter.Operator switch
            {
                ComparisonOperator.Equals => $"rv.version = {parameters.Add(ParseInt(filter.Value, filter.Field))}",
                ComparisonOperator.NotEquals => $"rv.version <> {parameters.Add(ParseInt(filter.Value, filter.Field))}",
                ComparisonOperator.In => $"rv.version IN ({string.Join(", ", ResolveInValues(filter.Value).Select(value => parameters.Add(ParseInt(value, filter.Field))))})",
                ComparisonOperator.Contains or ComparisonOperator.StartsWith => throw Unsupported(
                    "unsupported-metadata-contains-field",
                    "metadata field",
                    $"Metadata field '{filter.Field}' does not support text filtering in the SQL Server JSON query provider.",
                    "Filter.Field"),
                _ => throw UnsupportedMetadataOperator(filter.Operator),
            };
        }

        var text = SqlServerMetadataField.Text(filter.Field, "Filter.Field");
        return filter.Operator switch
        {
            ComparisonOperator.Equals => TextEquals(text, filter.Value),
            ComparisonOperator.NotEquals => $"NOT ({TextEquals(text, filter.Value)})",
            ComparisonOperator.In => AnyOf(ResolveInValues(filter.Value).Select(value => TextEquals(text, value))),
            ComparisonOperator.Contains => TextContains(text, filter.Value),
            ComparisonOperator.StartsWith => TextStartsWith(text, filter.Value),
            _ => throw UnsupportedMetadataOperator(filter.Operator),
        };
    }

    private string TranslateAspectPresence(AspectPresenceFilter filter) =>
        $"EXISTS (SELECT 1 FROM OPENJSON(rv.payload, N'$.aspects') aspect WHERE aspect.[key] = {parameters.Add(filter.AspectKey)})";

    private string TranslateFacetValue(FacetValueFilter filter)
    {
        var value = facets.Get(filter.AspectKey, filter.FacetDefinitionId);

        return filter.Operator switch
        {
            ComparisonOperator.Equals => FacetEquals(value, filter.Value),
            ComparisonOperator.NotEquals => SqlServerText.Bool($"{value.IsPresent} AND NOT ({FacetEqualsCondition(value, filter.Value)})"),
            ComparisonOperator.In => AnyOf(ResolveInValues(filter.Value).Select(candidate => FacetEquals(value, candidate))),
            ComparisonOperator.Contains => TextContains(value.Text, filter.Value),
            ComparisonOperator.StartsWith => TextStartsWith(value.Text, filter.Value),
            // An empty array does not exist.
            ComparisonOperator.Exists => SqlServerText.Bool(
                $"({value.Type} = 4 AND EXISTS (SELECT 1 FROM OPENJSON({value.ArrayJson}))) OR {value.Type} IN (1, 2, 3, 5)"),
            ComparisonOperator.ArrayContains => ArrayContains(value, [filter.Value], requireAll: false),
            ComparisonOperator.ArrayContainsAny => ArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: false),
            ComparisonOperator.ArrayContainsAll => ArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: true),
            ComparisonOperator.Range => Range(value, RequireRange(filter.Value)),
            _ => throw Unsupported(
                "unsupported-comparison-operator",
                "comparison operator",
                $"Comparison operator '{filter.Operator}' is not supported for facet filters by the SQL Server JSON query provider.",
                "Filter.Operator")
        };
    }

    // The conjunct is implied by the predicate, so adding it changes no result; under NOT it could turn an unknown into
    // false, so it is only added where the predicate isn't negated.
    private string WithIndex(string predicate, FilterExpression filter, bool positive) =>
        positive && conjuncts.For(filter) is { } conjunct ? $"({predicate}) AND ({conjunct})" : predicate;

    private string TranslateLogical(LogicalExpression expression, bool positive) => expression.Operator switch
    {
        LogicalOperator.And => JoinLogical("AND", expression.Operands, positive),
        LogicalOperator.Or => JoinLogical("OR", expression.Operands, positive),
        LogicalOperator.Not when expression.Operands is [var operand] => $"NOT ({Translate(operand, !positive)})",
        LogicalOperator.Not => throw Unsupported(
            "invalid-not-operands",
            "logical expression",
            "NOT logical expressions require exactly one operand.",
            "Filter.Operands"),
        _ => throw Unsupported(
            "unsupported-logical-operator",
            "logical operator",
            $"Logical operator '{expression.Operator}' is not supported by the SQL Server JSON query provider.",
            "Filter.Operator")
    };

    private string JoinLogical(string sqlOperator, IReadOnlyList<FilterExpression> operands, bool positive)
    {
        if (operands.Count == 0)
            throw Unsupported(
                "empty-logical-operands",
                "logical expression",
                $"{sqlOperator} logical expressions require at least one operand.",
                "Filter.Operands");

        return string.Join($" {sqlOperator} ", operands.Select(operand => $"({Translate(operand, positive)})"));
    }

    // Unknown for a missing facet when the expected value is numeric (see WhenNumber), as in the other providers.
    private string FacetEquals(SqlServerFacetValue value, object? expected) =>
        TryConvertDouble(expected, out var number)
            ? value.WhenNumber($"{value.Number} = {parameters.Add(number)}")
            : TextEquals(value.Text, expected);

    // The same test as a plain condition, for use where the facet is known to be present.
    private string FacetEqualsCondition(SqlServerFacetValue value, object? expected) =>
        TryConvertDouble(expected, out var number)
            ? $"{value.Type} = 2 AND {value.Number} = {parameters.Add(number)}"
            : TextEquals(value.Text, expected);

    private string Range(SqlServerFacetValue value, RangeValue range)
    {
        if (ResolveRangeKind(range) == RelationalRangeKind.DateTime)
            return DateTimeRange(value, range);

        var bounds = new List<string>();
        if (range.Min is not null)
            bounds.Add($"{value.Number} {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDouble(range.Min, "minimum range bound"))}");

        if (range.Max is not null)
            bounds.Add($"{value.Number} {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDouble(range.Max, "maximum range bound"))}");

        return value.WhenNumber(string.Join(" AND ", bounds));
    }

    private string DateTimeRange(SqlServerFacetValue value, RangeValue range)
    {
        var key = value.DateKey;
        var bounds = new List<string> { $"{value.Type} = 1", $"{key} IS NOT NULL" };
        if (range.Min is not null)
            bounds.Add($"{key} {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDateKey(range.Min, "minimum range bound", "Filter.Value.Min"))}");

        if (range.Max is not null)
            bounds.Add($"{key} {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDateKey(range.Max, "maximum range bound", "Filter.Value.Max"))}");

        return SqlServerText.Bool(string.Join(" AND ", bounds));
    }

    // Mirrors the SQLite translation and InMemoryQueryService.ArrayElementMatches: the candidate's QueryArrayValue
    // kind selects which JSON element type matches and how. A value that isn't an array never matches.
    private string ArrayContains(SqlServerFacetValue value, IReadOnlyList<object?> candidates, bool requireAll)
    {
        var matches = candidates.Select(ArrayElementMatch).ToList();
        var elements = $"OPENJSON({value.ArrayJson}) element";
        var predicate = requireAll
            ? string.Join(" AND ", matches.Select(match => $"EXISTS (SELECT 1 FROM {elements} WHERE {match})"))
            : $"EXISTS (SELECT 1 FROM {elements} WHERE {string.Join(" OR ", matches.Select(static match => $"({match})"))})";

        return SqlServerText.Bool($"{value.Type} = 4 AND ({predicate})");
    }

    private string ArrayElementMatch(object? candidate) => ClassifyArrayCandidate(candidate) switch
    {
        QueryArrayElementKind.Text =>
            $"element.[type] = 1 AND {SqlServerText.EqualsIgnoreCaseCondition("element.[value]", parameters.Add(QueryArrayValue.FormatText(candidate!)))}",
        // Whole numbers compare as decimals, so integer elements match exactly beyond 2^53 (and 2.0 equals 2);
        // numbers decimal can't hold (exponents) fall back to double.
        QueryArrayElementKind.Numeric when QueryArrayValue.TryGetIntegral(candidate!, out var integral) =>
            $"element.[type] = 2 AND (TRY_CONVERT(decimal(38, 18), element.[value]) = {parameters.Add((decimal)integral)} OR (TRY_CONVERT(decimal(38, 18), element.[value]) IS NULL AND TRY_CONVERT(float, element.[value]) = {parameters.Add((double)integral)}))",
        QueryArrayElementKind.Numeric =>
            $"element.[type] = 2 AND TRY_CONVERT(float, element.[value]) = {parameters.Add(Convert.ToDouble(candidate, CultureInfo.InvariantCulture))}",
        QueryArrayElementKind.Boolean =>
            $"element.[type] = 3 AND element.[value] = {(candidate is true ? "N'true'" : "N'false'")}",
        _ =>
            $"element.[type] = 1 AND {SqlServerDateKey.Of("element.[value]")} = {parameters.Add(ConvertToDateKey(candidate!, "array candidate", "Filter.Value"))}",
    };

    // A NULL expected value equals only a NULL actual value, as string.Equals(null, null) does.
    private string TextEquals(string actualSql, object? expected) =>
        FormatValue(expected) is { } text
            ? SqlServerText.EqualsIgnoreCase(actualSql, parameters.Add(text))
            : SqlServerText.Bool($"({actualSql}) IS NULL");

    private string TextContains(string actualSql, object? expected) =>
        FormatValue(expected) is { } text ? SqlServerText.ContainsIgnoreCase(actualSql, parameters.Add(text)) : "1 = 0";

    private string TextStartsWith(string actualSql, object? expected) =>
        FormatValue(expected) is { } text ? SqlServerText.StartsWithIgnoreCase(actualSql, parameters.Add(text)) : "1 = 0";

    private static string AnyOf(IEnumerable<string> predicates) =>
        string.Join(" OR ", predicates.Select(static predicate => $"({predicate})"));

    private static Aster.Core.Exceptions.UnsupportedQueryFeatureException UnsupportedMetadataOperator(ComparisonOperator op) =>
        Unsupported(
            "unsupported-comparison-operator",
            "comparison operator",
            $"Comparison operator '{op}' is not supported for metadata filters by the SQL Server JSON query provider.",
            "Filter.Operator");
}
