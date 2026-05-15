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
                $"aster_text_equals(CAST({column} AS TEXT), CAST({parameters.Add(filter.Value)} AS TEXT))",
            ComparisonOperator.Contains when !SqliteMetadataField.IsNumeric(filter.Field) =>
                $"aster_text_contains(CAST({column} AS TEXT), CAST({parameters.Add(filter.Value)} AS TEXT))",
            ComparisonOperator.Contains =>
                throw Unsupported(
                    "unsupported-metadata-contains-field",
                    "metadata field",
                    $"Metadata field '{filter.Field}' does not support containment filtering in the SQLite JSON query provider.",
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
        var value = FacetValueSql(filter.AspectKey, filter.FacetDefinitionId);

        return filter.Operator switch
        {
            ComparisonOperator.Equals when TryConvertDouble(filter.Value, out var number) =>
                $"{value.IsNumericPredicate} AND CAST({value.ValueExpression} AS REAL) = {parameters.Add(number)}",
            ComparisonOperator.Equals =>
                $"aster_text_equals(CAST({value.ValueExpression} AS TEXT), CAST({parameters.Add(FormatValue(filter.Value))} AS TEXT))",
            ComparisonOperator.Contains =>
                $"aster_text_contains(CAST({value.ValueExpression} AS TEXT), CAST({parameters.Add(FormatValue(filter.Value))} AS TEXT))",
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

    private FacetValueSqlExpression FacetValueSql(string aspectKey, string facetDefinitionId)
    {
        var aspectsPath = parameters.Add(SqliteJsonPath.Aspects);
        var typeAspectsPath = parameters.Add(SqliteJsonPath.Aspects);
        var aspectKeyParameter = parameters.Add(aspectKey);
        var typeAspectKeyParameter = parameters.Add(aspectKey);
        var facetKeys = SqliteJsonPath.FacetCandidates(facetDefinitionId).ToList();
        var facetKeyParameters = facetKeys.Select(parameters.Add).ToList();
        var typeFacetKeyParameters = facetKeys.Select(parameters.Add).ToList();
        var facetKeyList = string.Join(", ", facetKeyParameters);
        var typeFacetKeyList = string.Join(", ", typeFacetKeyParameters);

        return new($"""
            (
                SELECT facet.value
                FROM json_each(json_extract(rv.payload, {aspectsPath})) aspect
                JOIN json_each(aspect.value) facet
                WHERE aspect.key = {aspectKeyParameter}
                  AND facet.key IN ({facetKeyList})
                ORDER BY CASE facet.key WHEN {facetKeyParameters[0]} THEN 0 ELSE 1 END
                LIMIT 1
            )
            """,
            $"""
            (
                SELECT facet.type
                FROM json_each(json_extract(rv.payload, {typeAspectsPath})) aspect
                JOIN json_each(aspect.value) facet
                WHERE aspect.key = {typeAspectKeyParameter}
                  AND facet.key IN ({typeFacetKeyList})
                ORDER BY CASE facet.key WHEN {typeFacetKeyParameters[0]} THEN 0 ELSE 1 END
                LIMIT 1
            ) IN ('integer', 'real')
            """);
    }

    private string TranslateRange(FacetValueSqlExpression value, RangeValue range)
    {
        var predicates = new List<string> { value.IsNumericPredicate };

        if (range.Min is not null)
            predicates.Add($"CAST({value.ValueExpression} AS REAL) {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDouble(range.Min, "minimum range bound"))}");

        if (range.Max is not null)
            predicates.Add($"CAST({value.ValueExpression} AS REAL) {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDouble(range.Max, "maximum range bound"))}");

        if (predicates.Count == 0)
            throw Unsupported(
                "empty-range",
                "value shape",
                "Range predicates require at least one bound.",
                "Filter.Value");

        return string.Join(" AND ", predicates);
    }

    private static int ParseInt(string value, string field) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
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

    private sealed record FacetValueSqlExpression(string ValueExpression, string IsNumericPredicate);
}
