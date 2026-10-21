using System.Collections;
using System.Globalization;
using Aster.Core.Exceptions;
using Aster.Core.Models.Querying;

namespace Aster.Persistence.Relational.Querying;

/// <summary>
/// Value-shape rules the relational query translators share, so every provider rejects the same malformed
/// predicates with the same codes and paths.
/// </summary>
internal static class RelationalQueryValues
{
    public static IReadOnlyList<object?> ResolveInValues(object? value)
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

    public static IReadOnlyList<object?> ResolveArrayCandidates(object? value)
    {
        if (value is null or string || value is not IEnumerable enumerable)
            throw Unsupported(
                "array-values-required",
                "value shape",
                "ArrayContainsAny and ArrayContainsAll predicates require a non-string enumerable value set.",
                "Filter.Value");

        var candidates = enumerable.Cast<object?>().ToList();
        if (candidates.Count == 0)
            throw Unsupported(
                "empty-array-values",
                "value shape",
                "ArrayContainsAny and ArrayContainsAll predicates require at least one candidate value.",
                "Filter.Value");

        return candidates;
    }

    public static QueryArrayElementKind ClassifyArrayCandidate(object? candidate) =>
        QueryArrayValue.TryClassify(candidate, out var kind)
            ? kind
            : throw Unsupported(
                "unsupported-array-value-shape",
                "value shape",
                "Array candidates must be string, GUID, numeric, boolean, or date/time values.",
                "Filter.Value");

    public static void ValidateRange(FilterExpression expression)
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
                ValidateRange(operand);
        }
    }

    public static RangeValue RequireRange(object? value) =>
        value as RangeValue
        ?? throw Unsupported(
            "range-value-required",
            "value shape",
            "Range predicates require a RangeValue.",
            "Filter.Value");

    public static int ParseInt(object? value, string field) =>
        int.TryParse(FormatValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw Unsupported(
                "invalid-metadata-value",
                "metadata value",
                $"Metadata field '{field}' requires an integer value.",
                "Filter.Value");

    public static bool TryConvertDouble(object? value, out double result)
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

    public static double ConvertToDouble(object value, string description) =>
        TryConvertDouble(value, out var result)
            ? result
            : throw Unsupported(
                "unsupported-range-value-shape",
                "value shape",
                $"{description} must be numeric.",
                "Filter.Value");

    public static string ConvertToDateKey(object value, string description, string path) =>
        QueryDateTimeValue.TryNormalizeDateKey(value, out var key)
            ? key
            : throw Unsupported(
                "unsupported-range-value-shape",
                "value shape",
                $"{description} must be a date-like value.",
                path);

    public static RelationalRangeKind ResolveRangeKind(RangeValue range)
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
            (RelationalRangeKind.Numeric, null) or (null, RelationalRangeKind.Numeric) or (RelationalRangeKind.Numeric, RelationalRangeKind.Numeric) =>
                RelationalRangeKind.Numeric,
            (RelationalRangeKind.DateTime, null) or (null, RelationalRangeKind.DateTime) or (RelationalRangeKind.DateTime, RelationalRangeKind.DateTime) =>
                RelationalRangeKind.DateTime,
            _ => throw Unsupported(
                "mixed-range-value-shapes",
                "value shape",
                "Range bounds must use the same value shape.",
                "Filter.Value"),
        };
    }

    public static string? FormatValue(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static UnsupportedQueryFeatureException Unsupported(
        string code,
        string feature,
        string message,
        string? path = null) =>
        new(code, feature, message, path);

    private static RelationalRangeKind? ResolveRangeBoundKind(object? value, string description, string path)
    {
        if (value is null)
            return null;

        if (TryConvertDouble(value, out _))
            return RelationalRangeKind.Numeric;

        if (QueryDateTimeValue.TryNormalizeDateKey(value, out _))
            return RelationalRangeKind.DateTime;

        throw Unsupported(
            "unsupported-range-value-shape",
            "value shape",
            $"{description} must be numeric or date-like.",
            path);
    }
}

internal enum RelationalRangeKind
{
    Numeric,
    DateTime,
}
