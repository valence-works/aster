using System.Globalization;
using Aster.Core.Models.Querying;
using Aster.Persistence.Relational.Querying;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.PostgresJsonb.Querying;

/// <summary>
/// Translates a filter to a parameterized predicate over <c>rv.payload</c>. It mirrors the SQLite translation
/// predicate for predicate, including where SQL's NULL logic makes a missing facet NULL rather than false, so the
/// two providers return the same rows under NOT as well.
/// </summary>
internal sealed class PostgresWhereTranslator(PostgresParameterBag parameters)
{
    public string Translate(FilterExpression expression) => expression switch
    {
        MetadataFilter metadata => TranslateMetadata(metadata),
        AspectPresenceFilter aspect => TranslateAspectPresence(aspect),
        FacetValueFilter facet => TranslateFacetValue(facet),
        LogicalExpression logical => TranslateLogical(logical),
        _ => throw Unsupported(
            "unsupported-filter-type",
            "predicate",
            $"Filter expression '{expression.GetType().Name}' is not supported by the PostgreSQL JSONB query provider.",
            "Filter")
    };

    public void Validate(FilterExpression expression) => ValidateRange(expression);

    private string TranslateMetadata(MetadataFilter filter)
    {
        if (PostgresMetadataField.IsNumeric(filter.Field))
        {
            return filter.Operator switch
            {
                ComparisonOperator.Equals => $"rv.version = {parameters.Add(ParseInt(filter.Value, filter.Field))}",
                ComparisonOperator.NotEquals => $"rv.version <> {parameters.Add(ParseInt(filter.Value, filter.Field))}",
                ComparisonOperator.In => $"rv.version IN ({string.Join(", ", ResolveInValues(filter.Value).Select(value => parameters.Add(ParseInt(value, filter.Field))))})",
                ComparisonOperator.Contains or ComparisonOperator.StartsWith => throw Unsupported(
                    "unsupported-metadata-contains-field",
                    "metadata field",
                    $"Metadata field '{filter.Field}' does not support text filtering in the PostgreSQL JSONB query provider.",
                    "Filter.Field"),
                _ => throw UnsupportedMetadataOperator(filter.Operator),
            };
        }

        var text = PostgresMetadataField.Text(filter.Field, "Filter.Field");
        return filter.Operator switch
        {
            ComparisonOperator.Equals => TextEquals(text, filter.Value),
            ComparisonOperator.NotEquals => $"NOT {TextEquals(text, filter.Value)}",
            ComparisonOperator.In => AnyOf(ResolveInValues(filter.Value).Select(value => TextEquals(text, value))),
            ComparisonOperator.Contains => TextContains(text, filter.Value),
            ComparisonOperator.StartsWith => TextStartsWith(text, filter.Value),
            _ => throw UnsupportedMetadataOperator(filter.Operator),
        };
    }

    private string TranslateAspectPresence(AspectPresenceFilter filter) =>
        $"COALESCE(jsonb_exists(rv.payload -> 'aspects', {parameters.Add(filter.AspectKey)}), false)";

    private string TranslateFacetValue(FacetValueFilter filter)
    {
        var value = PostgresFacetValue.Create(parameters, filter.AspectKey, filter.FacetDefinitionId);

        return filter.Operator switch
        {
            ComparisonOperator.Equals => FacetEquals(value, filter.Value),
            ComparisonOperator.NotEquals => $"{value.IsPresent} AND NOT {FacetEquals(value, filter.Value)}",
            ComparisonOperator.In => AnyOf(ResolveInValues(filter.Value).Select(candidate => FacetEquals(value, candidate))),
            ComparisonOperator.Contains => TextContains(value.Text, filter.Value),
            ComparisonOperator.StartsWith => TextStartsWith(value.Text, filter.Value),
            // An empty array does not exist.
            ComparisonOperator.Exists =>
                $"(CASE WHEN {value.Type} = 'array' THEN jsonb_array_length({value.Json}) > 0 ELSE {value.IsPresent} END)",
            ComparisonOperator.ArrayContains => ArrayContains(value, [filter.Value], requireAll: false),
            ComparisonOperator.ArrayContainsAny => ArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: false),
            ComparisonOperator.ArrayContainsAll => ArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: true),
            ComparisonOperator.Range => Range(value, RequireRange(filter.Value)),
            _ => throw Unsupported(
                "unsupported-comparison-operator",
                "comparison operator",
                $"Comparison operator '{filter.Operator}' is not supported for facet filters by the PostgreSQL JSONB query provider.",
                "Filter.Operator")
        };
    }

    private string TranslateLogical(LogicalExpression expression) => expression.Operator switch
    {
        LogicalOperator.And => JoinLogical("AND", expression.Operands),
        LogicalOperator.Or => JoinLogical("OR", expression.Operands),
        LogicalOperator.Not when expression.Operands is [var operand] => $"NOT ({Translate(operand)})",
        LogicalOperator.Not => throw Unsupported(
            "invalid-not-operands",
            "logical expression",
            "NOT logical expressions require exactly one operand.",
            "Filter.Operands"),
        _ => throw Unsupported(
            "unsupported-logical-operator",
            "logical operator",
            $"Logical operator '{expression.Operator}' is not supported by the PostgreSQL JSONB query provider.",
            "Filter.Operator")
    };

    private string JoinLogical(string sqlOperator, IReadOnlyList<FilterExpression> operands)
    {
        if (operands.Count == 0)
            throw Unsupported(
                "empty-logical-operands",
                "logical expression",
                $"{sqlOperator} logical expressions require at least one operand.",
                "Filter.Operands");

        return string.Join($" {sqlOperator} ", operands.Select(operand => $"({Translate(operand)})"));
    }

    private string FacetEquals(PostgresFacetValue value, object? expected) =>
        TryConvertDouble(expected, out var number)
            ? value.WhenNumber($"{value.Number} = {parameters.Add(number)}")
            : TextEquals(value.Text, expected);

    private string Range(PostgresFacetValue value, RangeValue range)
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

    private string DateTimeRange(PostgresFacetValue value, RangeValue range)
    {
        var key = PostgresDateKey.Sql($"{value.Json} #>> '{{}}'");
        var bounds = new List<string> { $"{key} IS NOT NULL" };
        if (range.Min is not null)
            bounds.Add($"{key} COLLATE \"C\" {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDateKey(range.Min, "minimum range bound", "Filter.Value.Min"))}");

        if (range.Max is not null)
            bounds.Add($"{key} COLLATE \"C\" {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDateKey(range.Max, "maximum range bound", "Filter.Value.Max"))}");

        return $"(CASE WHEN {value.Type} = 'string' THEN {string.Join(" AND ", bounds)} ELSE false END)";
    }

    // Mirrors the SQLite translation and InMemoryQueryService.ArrayElementMatches: the candidate's QueryArrayValue
    // kind selects which JSON element type matches and how. A value that isn't an array never matches.
    private string ArrayContains(PostgresFacetValue value, IReadOnlyList<object?> candidates, bool requireAll)
    {
        var matches = candidates.Select(ArrayElementMatch).ToList();
        var elements = $"jsonb_array_elements({value.Json}) element(value)";
        var predicate = requireAll
            ? string.Join(" AND ", matches.Select(match => $"EXISTS (SELECT 1 FROM {elements} WHERE {match})"))
            : $"EXISTS (SELECT 1 FROM {elements} WHERE {AnyOf(matches)})";

        return $"(CASE WHEN {value.Type} = 'array' THEN ({predicate}) ELSE false END)";
    }

    private string ArrayElementMatch(object? candidate) => ClassifyArrayCandidate(candidate) switch
    {
        QueryArrayElementKind.Text =>
            $"jsonb_typeof(element.value) = 'string' AND {PostgresText.EqualsIgnoreCase("element.value #>> '{}'", parameters.Add(QueryArrayValue.FormatText(candidate!)))}",
        // jsonb compares numbers exactly, so whole numbers match integer elements beyond 2^53 (and 2.0 equals 2).
        QueryArrayElementKind.Numeric when QueryArrayValue.TryGetIntegral(candidate!, out var integral) =>
            $"jsonb_typeof(element.value) = 'number' AND element.value = to_jsonb({parameters.Add(integral)}::bigint)",
        QueryArrayElementKind.Numeric =>
            $"jsonb_typeof(element.value) = 'number' AND (element.value #>> '{{}}')::double precision = {parameters.Add(Convert.ToDouble(candidate, CultureInfo.InvariantCulture))}",
        QueryArrayElementKind.Boolean =>
            candidate is true ? "element.value = 'true'::jsonb" : "element.value = 'false'::jsonb",
        _ =>
            $"jsonb_typeof(element.value) = 'string' AND {PostgresDateKey.Sql("element.value #>> '{}'")} = {parameters.Add(ConvertToDateKey(candidate!, "array candidate", "Filter.Value"))}",
    };

    // A NULL expected value equals only a NULL actual value, as string.Equals(null, null) does.
    private string TextEquals(string actualSql, object? expected) =>
        FormatValue(expected) is { } text
            ? PostgresText.EqualsIgnoreCase(actualSql, parameters.Add(text))
            : $"(({actualSql}) IS NULL)";

    private string TextContains(string actualSql, object? expected) =>
        FormatValue(expected) is { } text ? PostgresText.ContainsIgnoreCase(actualSql, parameters.Add(text)) : "false";

    private string TextStartsWith(string actualSql, object? expected) =>
        FormatValue(expected) is { } text ? PostgresText.StartsWithIgnoreCase(actualSql, parameters.Add(text)) : "false";

    private static string AnyOf(IEnumerable<string> predicates) =>
        string.Join(" OR ", predicates.Select(predicate => $"({predicate})"));

    private static Aster.Core.Exceptions.UnsupportedQueryFeatureException UnsupportedMetadataOperator(ComparisonOperator op) =>
        Unsupported(
            "unsupported-comparison-operator",
            "comparison operator",
            $"Comparison operator '{op}' is not supported for metadata filters by the PostgreSQL JSONB query provider.",
            "Filter.Operator");
}
