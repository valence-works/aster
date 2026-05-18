using System.Collections;
using System.Globalization;
using Aster.Core.Exceptions;
using Aster.Core.Models.Querying;

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

    public void Validate(FilterExpression expression)
    {
        if (expression is FacetValueFilter { Operator: ComparisonOperator.Range, Value: RangeValue { Min: null, Max: null } })
            throw Unsupported(
                "empty-range",
                "value shape",
                "Range predicates require at least one bound.",
                "Filter.Value");

        if (expression is LogicalExpression logical)
        {
            foreach (var operand in logical.Operands)
                Validate(operand);
        }
    }

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
            ComparisonOperator.Exists =>
                $"{value.Value} IS NOT NULL",
            ComparisonOperator.Range when filter.Value is RangeValue range =>
                TranslateRange(value, range),
            ComparisonOperator.Range =>
                throw Unsupported(
                    "range-value-required",
                    "value shape",
                    "Range predicates require a RangeValue.",
                    "Filter.Value"),
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
        if (rangeKind == RangeKind.DateTime)
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
            predicates.Add($"{dateKey} {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDateKey(range.Min, "minimum range bound"))}");

        if (range.Max is not null)
            predicates.Add($"{dateKey} {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDateKey(range.Max, "maximum range bound"))}");

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

    private static IReadOnlyList<object?> ResolveInValues(object? value)
    {
        if (value is string || value is not IEnumerable enumerable)
            throw Unsupported(
                "in-values-required",
                "value shape",
                "In predicates require a non-string enumerable value set.",
                "Filter.Value");

        var values = new List<object?>();
        foreach (var item in enumerable)
            values.Add(item);

        if (values.Count == 0)
            throw Unsupported(
                "empty-in-values",
                "value shape",
                "In predicates require at least one candidate value.",
                "Filter.Value");

        return values;
    }

    private static int ParseInt(object? value, string field) =>
        int.TryParse(FormatValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw Unsupported(
                "invalid-metadata-value",
                "metadata value",
                $"Metadata field '{field}' requires an integer value.",
                "Filter.Value");

    private static bool TryConvertDouble(object? value, out double result)
    {
        if (value is null)
        {
            result = default;
            return false;
        }

        try
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return value is not string || double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
        catch (InvalidCastException)
        {
            result = default;
            return false;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static double ConvertToDouble(object value, string description) =>
        TryConvertDouble(value, out var result)
            ? result
            : throw Unsupported(
                "unsupported-range-value-shape",
                "value shape",
                $"{description} must be numeric.",
                "Filter.Value");

    private static string ConvertToDateKey(object value, string description) =>
        SqliteDateTimeBehavior.TryNormalizeDateKey(value, out var key)
            ? key
            : throw Unsupported(
                "unsupported-range-value-shape",
                "value shape",
                $"{description} must be a date-like value.",
                "Filter.Value");

    private static RangeKind ResolveRangeKind(RangeValue range)
    {
        var minKind = ResolveRangeBoundKind(range.Min, "minimum range bound", "Filter.Value.Min");
        var maxKind = ResolveRangeBoundKind(range.Max, "maximum range bound", "Filter.Value.Max");

        return (minKind, maxKind) switch
        {
            (null, null) => throw Unsupported(
                "empty-range",
                "value shape",
                "Range predicates require at least one bound.",
                "Filter.Value"),
            (RangeKind.Numeric, null) or (null, RangeKind.Numeric) or (RangeKind.Numeric, RangeKind.Numeric) =>
                RangeKind.Numeric,
            (RangeKind.DateTime, null) or (null, RangeKind.DateTime) or (RangeKind.DateTime, RangeKind.DateTime) =>
                RangeKind.DateTime,
            _ => throw Unsupported(
                "mixed-range-value-shapes",
                "value shape",
                "Range bounds must use the same value shape.",
                "Filter.Value"),
        };
    }

    private static RangeKind? ResolveRangeBoundKind(object? value, string description, string path)
    {
        if (value is null)
            return null;

        if (TryConvertDouble(value, out _))
            return RangeKind.Numeric;

        if (SqliteDateTimeBehavior.TryNormalizeDateKey(value, out _))
            return RangeKind.DateTime;

        throw Unsupported(
            "unsupported-range-value-shape",
            "value shape",
            $"{description} must be numeric or date-like.",
            path);
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static UnsupportedQueryFeatureException Unsupported(
        string code,
        string feature,
        string message,
        string? path = null) =>
        new(code, feature, message, path);

    private enum RangeKind
    {
        Numeric,
        DateTime,
    }
}
