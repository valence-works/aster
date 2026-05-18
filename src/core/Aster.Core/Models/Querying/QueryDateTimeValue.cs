using System.Globalization;

namespace Aster.Core.Models.Querying;

/// <summary>
/// Shared date/time value-shape rules for portable query validation and providers.
/// </summary>
public static class QueryDateTimeValue
{
    private static readonly string[] AcceptedDateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    /// <summary>
    /// Returns whether <paramref name="value"/> is an accepted date/time string for query ranges.
    /// </summary>
    /// <param name="value">The candidate date/time string.</param>
    /// <returns><see langword="true"/> when the value uses an accepted ISO-8601-style shape.</returns>
    public static bool IsAcceptedDateTimeString(string value) =>
        value.Contains('T', StringComparison.Ordinal)
        && DateTime.TryParseExact(
            value,
            AcceptedDateTimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);

    /// <summary>
    /// Attempts to normalize an accepted date/time value to a UTC key suitable for ordinal comparison.
    /// </summary>
    /// <param name="value">A <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, or accepted date/time string.</param>
    /// <param name="key">The normalized UTC key when successful.</param>
    /// <returns><see langword="true"/> when the value can be normalized.</returns>
    public static bool TryNormalizeDateKey(object? value, out string key)
    {
        switch (value)
        {
            case DateTimeOffset dateTimeOffset:
                key = FormatUtc(dateTimeOffset);
                return true;
            case DateTime dateTime:
                key = FormatUtc(new DateTimeOffset(NormalizeDateTime(dateTime)));
                return true;
            case string text when TryNormalizeTextDateKey(text, out key):
                return true;
            default:
                key = string.Empty;
                return false;
        }
    }

    private static bool TryNormalizeTextDateKey(string value, out string key)
    {
        if (value.Contains('T', StringComparison.Ordinal)
            && DateTimeOffset.TryParseExact(
                value,
                AcceptedDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dateTimeOffset))
        {
            key = FormatUtc(dateTimeOffset);
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static DateTime NormalizeDateTime(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
