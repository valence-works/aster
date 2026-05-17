using System.Globalization;

namespace Aster.Persistence.SqliteJson.Querying;

internal static class SqliteDateTimeBehavior
{
    public const string DateKeyFunction = "aster_datetime_key";

    public static string? NormalizeDateKey(string? value) =>
        TryNormalizeDateKey(value, out var key) ? key : null;

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
        if (!ContainsTimeComponent(value))
        {
            key = string.Empty;
            return false;
        }

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dateTimeOffset))
        {
            key = FormatUtc(dateTimeOffset);
            return true;
        }

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dateTime))
        {
            key = FormatUtc(new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)));
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static bool ContainsTimeComponent(string value) =>
        value.Contains('T', StringComparison.Ordinal)
        || value.Contains(' ', StringComparison.Ordinal);

    private static DateTime NormalizeDateTime(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
