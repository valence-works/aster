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
        _ => throw Unsupported($"Filter expression '{expression.GetType().Name}'")
    };

    public void Validate(FilterExpression expression)
    {
        if (expression is FacetValueFilter { Operator: ComparisonOperator.Range, Value: RangeValue { Min: null, Max: null } })
            throw Unsupported("Empty range predicates");

        if (expression is LogicalExpression logical)
        {
            foreach (var operand in logical.Operands)
                Validate(operand);
        }
    }

    private string TranslateMetadata(MetadataFilter filter)
    {
        var column = SqliteMetadataField.ResolveColumn(filter.Field, "Metadata field");

        return filter.Operator switch
        {
            ComparisonOperator.Equals when SqliteMetadataField.IsNumeric(filter.Field) =>
                $"{column} = {parameters.Add(ParseInt(filter.Value, filter.Field))}",
            ComparisonOperator.Equals =>
                $"LOWER(CAST({column} AS TEXT)) = LOWER(CAST({parameters.Add(filter.Value)} AS TEXT))",
            ComparisonOperator.Contains when !SqliteMetadataField.IsNumeric(filter.Field) =>
                $"LOWER(CAST({column} AS TEXT)) LIKE '%' || LOWER(CAST({parameters.Add(filter.Value)} AS TEXT)) || '%'",
            _ => throw Unsupported($"Metadata filter '{filter.Field}' with operator '{filter.Operator}'")
        };
    }

    private string TranslateAspectPresence(AspectPresenceFilter filter)
    {
        var path = parameters.Add(SqliteJsonPath.Aspect(filter.AspectKey));
        return $"json_type(rv.payload, {path}) IS NOT NULL";
    }

    private string TranslateFacetValue(FacetValueFilter filter)
    {
        var value = FacetValueSql(filter.AspectKey, filter.FacetDefinitionId);

        return filter.Operator switch
        {
            ComparisonOperator.Equals when TryConvertDouble(filter.Value, out var number) =>
                $"CAST({value} AS REAL) = {parameters.Add(number)}",
            ComparisonOperator.Equals =>
                $"LOWER(CAST({value} AS TEXT)) = LOWER(CAST({parameters.Add(FormatValue(filter.Value))} AS TEXT))",
            ComparisonOperator.Contains =>
                $"LOWER(CAST({value} AS TEXT)) LIKE '%' || LOWER(CAST({parameters.Add(FormatValue(filter.Value))} AS TEXT)) || '%'",
            ComparisonOperator.Range when filter.Value is RangeValue range =>
                TranslateRange(value, range),
            ComparisonOperator.Range =>
                throw Unsupported("Range predicates without RangeValue"),
            _ => throw Unsupported($"Facet filter operator '{filter.Operator}'")
        };
    }

    private string TranslateLogical(LogicalExpression expression)
    {
        return expression.Operator switch
        {
            LogicalOperator.And => JoinLogical("AND", expression.Operands),
            LogicalOperator.Or => JoinLogical("OR", expression.Operands),
            LogicalOperator.Not when expression.Operands is [var operand] => $"NOT ({Translate(operand)})",
            LogicalOperator.Not => throw Unsupported("NOT logical expressions with anything other than one operand"),
            _ => throw Unsupported($"Logical operator '{expression.Operator}'")
        };
    }

    private string JoinLogical(string sqlOperator, IReadOnlyList<FilterExpression> operands)
    {
        if (operands.Count == 0)
            throw Unsupported($"Empty {sqlOperator} logical expressions");

        return string.Join($" {sqlOperator} ", operands.Select(operand => $"({Translate(operand)})"));
    }

    private string FacetValueSql(string aspectKey, string facetDefinitionId)
    {
        var paths = SqliteJsonPath.FacetCandidates(aspectKey, facetDefinitionId)
            .Select(path => $"json_extract(rv.payload, {parameters.Add(path)})");

        return $"COALESCE({string.Join(", ", paths)})";
    }

    private string TranslateRange(string value, RangeValue range)
    {
        var predicates = new List<string>();

        if (range.Min is not null)
            predicates.Add($"CAST({value} AS REAL) {(range.IncludeMin ? ">=" : ">")} {parameters.Add(ConvertToDouble(range.Min, "minimum range bound"))}");

        if (range.Max is not null)
            predicates.Add($"CAST({value} AS REAL) {(range.IncludeMax ? "<=" : "<")} {parameters.Add(ConvertToDouble(range.Max, "maximum range bound"))}");

        if (predicates.Count == 0)
            throw Unsupported("Empty range predicates");

        return string.Join(" AND ", predicates);
    }

    private static int ParseInt(string value, string field) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw Unsupported($"Metadata field '{field}' requires an integer value");

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
            : throw Unsupported($"{description} must be numeric");

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static UnsupportedQueryFeatureException Unsupported(string feature) =>
        new($"{feature} is not supported by the SQLite JSON query provider.");
}
