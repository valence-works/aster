namespace Aster.Core.Models.Querying;

/// <summary>
/// Shared element-equality rules for the portable array operators
/// (<see cref="ComparisonOperator.ArrayContains"/>, <see cref="ComparisonOperator.ArrayContainsAny"/>,
/// <see cref="ComparisonOperator.ArrayContainsAll"/>). Queries are definition-agnostic, so the
/// candidate value's CLR type selects how stored JSON array elements are compared.
/// </summary>
public static class QueryArrayValue
{
    /// <summary>
    /// Classifies a candidate value for array element comparison.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="kind">The comparison kind when the value is supported.</param>
    /// <returns>
    /// <see langword="true"/> for strings and GUIDs (<see cref="QueryArrayElementKind.Text"/>), CLR numeric
    /// types (<see cref="QueryArrayElementKind.Numeric"/>), booleans (<see cref="QueryArrayElementKind.Boolean"/>),
    /// and <see cref="DateTime"/> / <see cref="DateTimeOffset"/> (<see cref="QueryArrayElementKind.DateTime"/>);
    /// <see langword="false"/> for <see langword="null"/> and every other shape.
    /// </returns>
    public static bool TryClassify(object? value, out QueryArrayElementKind kind)
    {
        switch (value)
        {
            case string or Guid:
                kind = QueryArrayElementKind.Text;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                kind = QueryArrayElementKind.Numeric;
                return true;
            case bool:
                kind = QueryArrayElementKind.Boolean;
                return true;
            case DateTime or DateTimeOffset:
                kind = QueryArrayElementKind.DateTime;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// Formats a <see cref="QueryArrayElementKind.Text"/> candidate for case-insensitive comparison;
    /// GUIDs use the hyphenated <c>D</c> form.
    /// </summary>
    /// <param name="value">A string or GUID candidate.</param>
    /// <returns>The text to compare against string elements.</returns>
    public static string FormatText(object value) => value switch
    {
        Guid guid => guid.ToString("D"),
        _ => (string)value,
    };
}

/// <summary>
/// How a candidate value is compared with stored JSON array elements.
/// </summary>
public enum QueryArrayElementKind
{
    /// <summary>Matches JSON string elements, compared ordinally ignoring case.</summary>
    Text,

    /// <summary>Matches JSON number elements with an equal numeric value (compared as double).</summary>
    Numeric,

    /// <summary>Matches JSON <c>true</c> or <c>false</c> elements with the same value.</summary>
    Boolean,

    /// <summary>
    /// Matches JSON string elements that denote the same UTC instant, using the accepted date/time
    /// shapes of <see cref="QueryDateTimeValue"/>.
    /// </summary>
    DateTime,
}
