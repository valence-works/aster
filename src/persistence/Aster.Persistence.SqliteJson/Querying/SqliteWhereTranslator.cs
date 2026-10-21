using System.Globalization;
using Aster.Core.Models.Querying;
using Aster.Persistence.Relational.Querying;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.SqliteJson.Querying;

internal sealed class SqliteWhereTranslator(SqliteParameterBag parameters)
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
            $"Filter expression '{expression.GetType().Name}' is not supported by the SQLite JSON query provider.",
            "Filter")
    };

    public void Validate(FilterExpression expression) => ValidateRange(expression);

    private string TranslateMetadata(MetadataFilter filter)
    {
        var column = SqliteMetadataField.ResolveColumn(
            filter.Field,
            "Metadata field",
            "unsupported-metadata-field",
            "metadata field",
            "Filter.Field");

        return filter.Operator switch
        {
            ComparisonOperator.Equals when SqliteMetadataField.IsNumeric(filter.Field) =>
                $"{column} = {parameters.Add(ParseInt(filter.Value, filter.Field))}",
            ComparisonOperator.Equals =>
                TextEquals($"CAST({column} AS TEXT)", filter.Value),
            ComparisonOperator.NotEquals when SqliteMetadataField.IsNumeric(filter.Field) =>
                $"{column} <> {parameters.Add(ParseInt(filter.Value, filter.Field))}",
            ComparisonOperator.NotEquals =>
                $"NOT {TextEquals($"CAST({column} AS TEXT)", filter.Value)}",
            ComparisonOperator.In when SqliteMetadataField.IsNumeric(filter.Field) =>
                TranslateNumericIn(column, filter.Value, candidate => ParseInt(candidate, filter.Field)),
            ComparisonOperator.In =>
                TranslateTextIn($"CAST({column} AS TEXT)", filter.Value),
            ComparisonOperator.Contains when !SqliteMetadataField.IsNumeric(filter.Field) =>
                TextContains($"CAST({column} AS TEXT)", filter.Value),
            ComparisonOperator.StartsWith when !SqliteMetadataField.IsNumeric(filter.Field) =>
                TextStartsWith($"CAST({column} AS TEXT)", filter.Value),
            ComparisonOperator.Contains or ComparisonOperator.StartsWith =>
                throw Unsupported(
                    "unsupported-metadata-contains-field",
                    "metadata field",
                    $"Metadata field '{filter.Field}' does not support text filtering in the SQLite JSON query provider.",
                    "Filter.Field"),
            _ => throw Unsupported(
                "unsupported-comparison-operator",
                "comparison operator",
                $"Comparison operator '{filter.Operator}' is not supported for metadata filters by the SQLite JSON query provider.",
                "Filter.Operator")
        };
    }

    private string TranslateAspectPresence(AspectPresenceFilter filter)
    {
        var path = parameters.Add(SqliteJsonPath.Aspects);
        var aspectKey = parameters.Add(filter.AspectKey);
        return $"EXISTS (SELECT 1 FROM json_each(json_extract(rv.payload, {path})) aspect WHERE aspect.key = {aspectKey})";
    }

    private string TranslateFacetValue(FacetValueFilter filter)
    {
        var value = SqliteFacetValueExpression.Create(parameters, filter.AspectKey, filter.FacetDefinitionId);

        return filter.Operator switch
        {
            ComparisonOperator.Equals when TryConvertDouble(filter.Value, out var number) =>
                $"{value.IsNumeric} AND CAST({value.Value} AS REAL) = {parameters.Add(number)}",
            ComparisonOperator.Equals =>
                TextEquals($"CAST({value.Value} AS TEXT)", filter.Value),
            ComparisonOperator.NotEquals when TryConvertDouble(filter.Value, out var number) =>
                $"{value.Value} IS NOT NULL AND NOT ({value.IsNumeric} AND CAST({value.Value} AS REAL) = {parameters.Add(number)})",
            ComparisonOperator.NotEquals =>
                $"{value.Value} IS NOT NULL AND NOT {TextEquals($"CAST({value.Value} AS TEXT)", filter.Value)}",
            ComparisonOperator.In =>
                TranslateFacetIn(value, filter.Value),
            ComparisonOperator.Contains =>
                TextContains($"CAST({value.Value} AS TEXT)", filter.Value),
            ComparisonOperator.StartsWith =>
                TextStartsWith($"CAST({value.Value} AS TEXT)", filter.Value),
            // An empty array does not exist; CASE keeps json_array_length away from non-JSON text values.
            ComparisonOperator.Exists =>
                $"CASE WHEN {value.Type} = 'array' THEN json_array_length({value.Value}) > 0 ELSE {value.Value} IS NOT NULL END",
            ComparisonOperator.ArrayContains =>
                TranslateArrayContains(value, [filter.Value], requireAll: false),
            ComparisonOperator.ArrayContainsAny =>
                TranslateArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: false),
            ComparisonOperator.ArrayContainsAll =>
                TranslateArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: true),
            ComparisonOperator.Range =>
                TranslateRange(value, RequireRange(filter.Value)),
            _ => throw Unsupported(
                "unsupported-comparison-operator",
                "comparison operator",
                $"Comparison operator '{filter.Operator}' is not supported for facet filters by the SQLite JSON query provider.",
                "Filter.Operator")
        };
    }

    private string TranslateLogical(LogicalExpression expression)
    {
        return expression.Operator switch
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
                $"Logical operator '{expression.Operator}' is not supported by the SQLite JSON query provider.",
                "Filter.Operator")
        };
    }

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

    private string TranslateRange(SqliteFacetValueExpression value, RangeValue range)
    {
        var rangeKind = ResolveRangeKind(range);
        if (rangeKind == RelationalRangeKind.DateTime)
            return TranslateDateTimeRange(value, range);

        var predicates = new List<string> { value.IsNumeric };

        if (range.Min is not null)
            predicates.Add($"CAST({value.Value} AS REAL) {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDouble(range.Min, "minimum range bound"))}");

        if (range.Max is not null)
            predicates.Add($"CAST({value.Value} AS REAL) {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDouble(range.Max, "maximum range bound"))}");

        if (predicates.Count == 0)
            throw Unsupported(
                "empty-range",
                "value shape",
                "Range predicates require at least one bound.",
                "Filter.Value");

        return string.Join(" AND ", predicates);
    }

    private string TranslateDateTimeRange(SqliteFacetValueExpression value, RangeValue range)
    {
        var dateKey = $"{SqliteDateTimeBehavior.DateKeyFunction}(CAST({value.Value} AS TEXT))";
        var predicates = new List<string>
        {
            $"{value.Type} = 'text'",
            $"{dateKey} IS NOT NULL",
        };

        if (range.Min is not null)
            predicates.Add($"{dateKey} {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDateKey(range.Min, "minimum range bound", "Filter.Value.Min"))}");

        if (range.Max is not null)
            predicates.Add($"{dateKey} {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDateKey(range.Max, "maximum range bound", "Filter.Value.Max"))}");

        return string.Join(" AND ", predicates);
    }

    private string TextEquals(string actualSql, object? expected) =>
        $"{SqliteTextBehavior.EqualsFunction}({actualSql}, CAST({parameters.Add(FormatValue(expected))} AS TEXT))";

    private string TextContains(string actualSql, object? expected) =>
        $"{SqliteTextBehavior.ContainsFunction}({actualSql}, CAST({parameters.Add(FormatValue(expected))} AS TEXT))";

    private string TextStartsWith(string actualSql, object? expected) =>
        $"{SqliteTextBehavior.StartsWithFunction}({actualSql}, CAST({parameters.Add(FormatValue(expected))} AS TEXT))";

    private string TranslateNumericIn<TNumber>(string actualSql, object? expected, Func<object?, TNumber> convert)
    {
        var parameterNames = ResolveInValues(expected)
            .Select(candidate => parameters.Add(convert(candidate)))
            .ToArray();

        return $"{actualSql} IN ({string.Join(", ", parameterNames)})";
    }

    private string TranslateTextIn(string actualSql, object? expected)
    {
        // Keep one UDF call per candidate so text IN follows the same null and case behavior as Equals.
        var predicates = ResolveInValues(expected)
            .Select(candidate => TextEquals(actualSql, candidate))
            .ToArray();

        return string.Join(" OR ", predicates.Select(predicate => $"({predicate})"));
    }

    private string TranslateFacetIn(SqliteFacetValueExpression value, object? expected)
    {
        var predicates = ResolveInValues(expected)
            .Select(candidate => TranslateFacetEquals(value, candidate))
            .ToArray();

        return string.Join(" OR ", predicates.Select(predicate => $"({predicate})"));
    }

    private string TranslateFacetEquals(SqliteFacetValueExpression value, object? expected) =>
        TryConvertDouble(expected, out var number)
            ? $"{value.IsNumeric} AND CAST({value.Value} AS REAL) = {parameters.Add(number)}"
            : TextEquals($"CAST({value.Value} AS TEXT)", expected);

    // Mirrors InMemoryQueryService.ArrayElementMatches: the candidate's QueryArrayValue kind selects
    // which JSON element type matches and how. CASE guards json_each from scalar facet values, which
    // are not JSON documents.
    private string TranslateArrayContains(SqliteFacetValueExpression value, IReadOnlyList<object?> candidates, bool requireAll)
    {
        var matches = candidates.Select(TranslateArrayElementMatch).ToList();
        var predicate = requireAll
            ? string.Join(" AND ", matches.Select(match => $"EXISTS (SELECT 1 FROM json_each({value.Value}) element WHERE {match})"))
            : $"EXISTS (SELECT 1 FROM json_each({value.Value}) element WHERE {string.Join(" OR ", matches.Select(match => $"({match})"))})";

        return $"CASE WHEN {value.Type} = 'array' THEN ({predicate}) ELSE 0 END";
    }

    private string TranslateArrayElementMatch(object? candidate) => ClassifyArrayCandidate(candidate) switch
    {
        QueryArrayElementKind.Text =>
            $"element.type = 'text' AND {SqliteTextBehavior.EqualsFunction}(element.value, CAST({parameters.Add(QueryArrayValue.FormatText(candidate!))} AS TEXT))",
        // Whole numbers bind as INTEGER so integer elements compare exactly beyond 2^53; SQLite still
        // compares an INTEGER parameter with a REAL element (e.g. 2.0) numerically.
        QueryArrayElementKind.Numeric when QueryArrayValue.TryGetIntegral(candidate!, out var integral) =>
            $"element.type IN ('integer', 'real') AND element.value = {parameters.Add(integral)}",
        QueryArrayElementKind.Numeric =>
            $"element.type IN ('integer', 'real') AND CAST(element.value AS REAL) = {parameters.Add(Convert.ToDouble(candidate, CultureInfo.InvariantCulture))}",
        QueryArrayElementKind.Boolean =>
            candidate is true ? "element.type = 'true'" : "element.type = 'false'",
        _ =>
            $"element.type = 'text' AND {SqliteDateTimeBehavior.DateKeyFunction}(element.value) = {parameters.Add(ConvertToDateKey(candidate!, "array candidate", "Filter.Value"))}",
    };
}
